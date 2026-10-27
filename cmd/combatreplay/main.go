package main

import (
	"bufio"
	"bytes"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"time"

	"partypilot/internal/combat"
)

func main() {
	path := flag.String("replay", "", "JSONL file of explicit synthetic combat worlds")
	feedPath := flag.String("feed-replay", "", "JSONL file of verified ordered combat feed frames")
	enabled := flag.Bool("enable-planning", false, "enable read-only default-profile plans")
	flag.Parse()
	if (*path == "") == (*feedPath == "") {
		fmt.Fprintln(os.Stderr, "usage: PartyPilot-CombatReplay (-replay worlds.jsonl | -feed-replay frames.jsonl) [-enable-planning]")
		os.Exit(2)
	}
	selectedPath := *path
	if *feedPath != "" {
		selectedPath = *feedPath
	}
	file, err := os.Open(selectedPath)
	if err != nil {
		fail(err)
	}
	defer file.Close()
	profile := combat.DefaultProfile()
	profile.Enabled = *enabled
	var encounters combat.EncounterTracker
	var targets combat.TargetCoordinator
	scanner := bufio.NewScanner(file)
	scanner.Buffer(make([]byte, 64*1024), 1024*1024)
	line := 0
	for scanner.Scan() {
		line++
		data := bytes.TrimSpace(scanner.Bytes())
		if len(data) == 0 {
			continue
		}
		decoder := json.NewDecoder(bytes.NewReader(data))
		decoder.DisallowUnknownFields()
		var world combat.World
		if *feedPath != "" {
			var record struct {
				Now       time.Time            `json:"now"`
				Frames    []combat.FeedFrame   `json:"frames"`
				Authority combat.FeedAuthority `json:"authority"`
			}
			if err := decoder.Decode(&record); err != nil {
				fail(fmt.Errorf("line %d: %w", line, err))
			}
			world, err = combat.AssembleWorld(record.Frames, record.Authority, record.Now)
			if err != nil {
				fail(fmt.Errorf("line %d: %w", line, err))
			}
		} else if err := decoder.Decode(&world); err != nil {
			fail(fmt.Errorf("line %d: %w", line, err))
		}
		var extra any
		if err := decoder.Decode(&extra); err != io.EOF {
			fail(fmt.Errorf("line %d: extra JSON value", line))
		}
		encounter, err := encounters.Update(world)
		if err != nil {
			fail(fmt.Errorf("line %d: %w", line, err))
		}
		plans, err := combat.EvaluateEncounter(profile, world, encounter)
		if err != nil {
			fail(fmt.Errorf("line %d: %w", line, err))
		}
		targets.Assign(profile, world, plans)
		result, err := json.Marshal(struct {
			Timestamp string            `json:"timestamp"`
			Area      string            `json:"areaInstance"`
			Encounter *combat.Encounter `json:"encounter,omitempty"`
			Plans     []combat.Plan     `json:"plans"`
		}{world.Timestamp.Format("2006-01-02T15:04:05.000Z07:00"), world.AreaInstance, encounter, plans})
		if err != nil {
			fail(err)
		}
		fmt.Println(string(result))
	}
	if err := scanner.Err(); err != nil {
		fail(err)
	}
}

func fail(err error) {
	fmt.Fprintln(os.Stderr, err)
	os.Exit(1)
}
