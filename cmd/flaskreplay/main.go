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
	Now        time.Time     `json:"now"`
	Player     int           `json:"player"`
	Vitals     flask.Vitals  `json:"vitals"`
	Support    *supportFrame `json:"support,omitempty"`
	MenuOpen   *bool         `json:"menu_open"`
	Recovering *bool         `json:"recovering"`
	Reset      bool          `json:"reset,omitempty"`
}

type supportFrame struct {
	Monitor        flask.SupportMonitor `json:"monitor"`
	TargetVitals   flask.Vitals         `json:"target_vitals"`
	SupportBuild   *bool                `json:"support_build"`
	BuildHasNoMana *bool                `json:"build_has_no_mana"`
	SustainedUse   *bool                `json:"sustained_use"`
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
		if input.MenuOpen == nil || input.Support == nil && input.Recovering == nil {
			return fmt.Errorf("line %d: menu_open and normal-frame recovering are required", line)
		}
		settings, err := flask.ResetProfiles(input.Player)
		if err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		settings.CombinedDefence = combinedDefence
		if input.Reset {
			engine.ResetPlayer(input.Player)
		}
		var activations []flask.Activation
		if input.Support != nil {
			support := input.Support
			if support.SupportBuild == nil || support.BuildHasNoMana == nil || support.SustainedUse == nil {
				return fmt.Errorf("line %d: support_build, build_has_no_mana, and sustained_use are required", line)
			}
			settings.SupportBuild = *support.SupportBuild
			settings.BuildHasNoMana = *support.BuildHasNoMana
			settings.SustainedUse = *support.SustainedUse
			activations, err = engine.StepSupport(input.Player, input.Now,
				support.TargetVitals, settings, support.Monitor, *input.MenuOpen)
		} else {
			activations, err = engine.Step(input.Player, input.Now, input.Vitals,
				settings, *input.MenuOpen, *input.Recovering)
		}
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
