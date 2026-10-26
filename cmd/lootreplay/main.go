package main

import (
	"bufio"
	"bytes"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"

	"partypilot/internal/autoloot"
)

type replayFrame struct {
	autoloot.Frame
	Reset bool `json:"reset,omitempty"`
}

func replay(reader io.Reader, writer io.Writer) error {
	gate := autoloot.NewGate()
	scanner := bufio.NewScanner(reader)
	scanner.Buffer(make([]byte, 64*1024), 1024*1024)
	encoder := json.NewEncoder(writer)
	for line := 1; scanner.Scan(); line++ {
		var input replayFrame
		decoder := json.NewDecoder(bytes.NewReader(scanner.Bytes()))
		decoder.DisallowUnknownFields()
		if err := decoder.Decode(&input); err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		var trailing any
		if err := decoder.Decode(&trailing); err != io.EOF {
			return fmt.Errorf("line %d: extra JSON after frame", line)
		}
		if input.Reset {
			if err := gate.Reset(input.Player); err != nil {
				return fmt.Errorf("line %d: %w", line, err)
			}
		}
		decision, err := gate.Step(input.Frame)
		if err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		if err := encoder.Encode(decision); err != nil {
			return err
		}
	}
	return scanner.Err()
}

func main() {
	path := flag.String("replay", "", "JSONL item-card frames with explicit pickup conditions")
	flag.Parse()
	if *path == "" {
		fatal(errors.New("-replay requires a JSONL file"))
	}
	file, err := os.Open(*path)
	if err != nil {
		fatal(err)
	}
	defer file.Close()
	if err := replay(file, os.Stdout); err != nil {
		fatal(err)
	}
}

func fatal(err error) {
	fmt.Fprintln(os.Stderr, err)
	os.Exit(1)
}
