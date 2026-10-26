package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"

	"partypilot/internal/follow"
)

type partyFrame struct {
	Commands map[int]follow.Command `json:"commands"`
}

type bridgeCommand struct {
	Slot int `json:"slot"`
	follow.Command
}

func selectCommands(reader io.Reader, writer io.Writer, slot int) error {
	scanner := bufio.NewScanner(reader)
	scanner.Buffer(make([]byte, 64*1024), 1024*1024)
	encoder := json.NewEncoder(writer)
	line := 0
	for scanner.Scan() {
		line++
		var frame partyFrame
		if err := json.Unmarshal(scanner.Bytes(), &frame); err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		command, exists := frame.Commands[slot]
		if !exists {
			command = follow.Command{State: follow.Waiting, Reason: "slot absent"}
		}
		if err := encoder.Encode(bridgeCommand{Slot: slot, Command: command}); err != nil {
			return err
		}
	}
	return scanner.Err()
}

func main() {
	slot := flag.Int("slot", 0, "follower slot to send to one virtual controller")
	flag.Parse()
	if *slot < 1 || *slot > 6 {
		fmt.Fprintln(os.Stderr, "specify a follower slot from 1 to 6")
		os.Exit(2)
	}
	if err := selectCommands(os.Stdin, os.Stdout, *slot); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
