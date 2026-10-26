package main

import (
	"bufio"
	"bytes"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"

	"partypilot/internal/combat"
)

func main() {
	path := flag.String("replay", "", "JSONL file of explicit synthetic combat worlds")
	enabled := flag.Bool("enable-planning", false, "enable read-only default-profile plans")
	flag.Parse()
	if *path == "" {
		fmt.Fprintln(os.Stderr, "usage: PartyPilot-CombatReplay -replay worlds.jsonl [-enable-planning]")
		os.Exit(2)
	}
	file, err := os.Open(*path)
	if err != nil {
		fail(err)
	}
	defer file.Close()
	profile := combat.DefaultProfile()
	profile.Enabled = *enabled
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
		if err := decoder.Decode(&world); err != nil {
			fail(fmt.Errorf("line %d: %w", line, err))
		}
		var extra any
		if err := decoder.Decode(&extra); err != io.EOF {
			fail(fmt.Errorf("line %d: extra JSON value", line))
		}
		plans, err := combat.Evaluate(profile, world)
		if err != nil {
			fail(fmt.Errorf("line %d: %w", line, err))
		}
		result, err := json.Marshal(struct {
			Timestamp string        `json:"timestamp"`
			Area      string        `json:"areaInstance"`
			Plans     []combat.Plan `json:"plans"`
		}{world.Timestamp.Format("2006-01-02T15:04:05.000Z07:00"), world.AreaInstance, plans})
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
