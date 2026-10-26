package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"os"
	"time"

	"partypilot/internal/flask"
)

type frame struct {
	Now        time.Time    `json:"now"`
	Player     int          `json:"player"`
	Vitals     flask.Vitals `json:"vitals"`
	MenuOpen   *bool        `json:"menu_open"`
	Recovering *bool        `json:"recovering"`
	Reset      bool         `json:"reset,omitempty"`
}

type result struct {
	Player      int                `json:"player"`
	Activations []flask.Activation `json:"activations"`
}

func run(path string, combinedDefence bool) error {
	if path == "" {
		return errors.New("-replay requires a JSONL file")
	}
	file, err := os.Open(path)
	if err != nil {
		return err
	}
	defer file.Close()
	engine := flask.New()
	scanner := bufio.NewScanner(file)
	scanner.Buffer(make([]byte, 64*1024), 1024*1024)
	encoder := json.NewEncoder(os.Stdout)
	line := 0
	for scanner.Scan() {
		line++
		var input frame
		if err := json.Unmarshal(scanner.Bytes(), &input); err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		if input.MenuOpen == nil || input.Recovering == nil {
			return fmt.Errorf("line %d: menu_open and recovering are required", line)
		}
		settings, err := flask.ResetProfiles(input.Player)
		if err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		settings.CombinedDefence = combinedDefence
		if input.Reset {
			engine.ResetPlayer(input.Player)
		}
		activations, err := engine.Step(input.Player, input.Now, input.Vitals, settings, *input.MenuOpen, *input.Recovering)
		if err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		if activations == nil {
			activations = []flask.Activation{}
		}
		if err := encoder.Encode(result{Player: input.Player, Activations: activations}); err != nil {
			return err
		}
	}
	return scanner.Err()
}

func main() {
	path := flag.String("replay", "", "JSONL frames with explicit menu and recovery state")
	combinedDefence := flag.Bool("combined-defence", false, "combine life and energy shield for life-flask decisions")
	flag.Parse()
	if err := run(*path, *combinedDefence); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
