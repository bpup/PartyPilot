package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"strconv"
	"strings"
	"time"

	"partypilot/internal/focus"
	"partypilot/internal/follow"
	"partypilot/internal/navigation"
	"partypilot/internal/party"
)

type frame struct {
	Now            time.Time               `json:"now"`
	Positions      map[int]follow.Position `json:"positions"`
	Focus          int                     `json:"focus,omitempty"`
	ReturnToLeader bool                    `json:"return_to_leader,omitempty"`
	ResetFollow    bool                    `json:"reset_follow,omitempty"`
	Terrain        *navigation.Grid        `json:"terrain,omitempty"`
}

type result struct {
	Focused  int                    `json:"focused"`
	Commands map[int]follow.Command `json:"commands"`
}

func parseRoster(value string) ([]focus.Player, error) {
	if value == "" {
		return nil, errors.New("empty roster")
	}
	var players []focus.Player
	for _, entry := range strings.Split(value, ",") {
		parts := strings.Split(entry, ":")
		if len(parts) != 2 {
			return nil, fmt.Errorf("invalid roster entry %q", entry)
		}
		slot, slotErr := strconv.Atoi(parts[0])
		client, clientErr := strconv.Atoi(parts[1])
		if slotErr != nil || clientErr != nil {
			return nil, fmt.Errorf("invalid roster entry %q", entry)
		}
		players = append(players, focus.Player{Slot: slot, Client: client})
	}
	return players, nil
}

func parseFollowTargets(value string) (map[int]int, error) {
	targets := make(map[int]int)
	if value == "" {
		return targets, nil
	}
	for _, entry := range strings.Split(value, ",") {
		parts := strings.Split(entry, ":")
		if len(parts) != 2 {
			return nil, fmt.Errorf("invalid follow target %q", entry)
		}
		slot, slotErr := strconv.Atoi(parts[0])
		target, targetErr := strconv.Atoi(parts[1])
		if slotErr != nil || targetErr != nil || slot < 1 || slot > 6 || target < 1 || target > 6 {
			return nil, fmt.Errorf("invalid follow target %q", entry)
		}
		if _, exists := targets[slot]; exists {
			return nil, fmt.Errorf("duplicate follow target for player %d", slot)
		}
		targets[slot] = target
	}
	return targets, nil
}

func replay(reader io.Reader, writer io.Writer, coordinator *party.Coordinator, pace time.Duration) error {
	scanner := bufio.NewScanner(reader)
	scanner.Buffer(make([]byte, 64*1024), 1024*1024)
	encoder := json.NewEncoder(writer)
	line := 0
	for scanner.Scan() {
		line++
		if line > 1 && pace > 0 {
			time.Sleep(pace)
		}
		var item frame
		if err := json.Unmarshal(scanner.Bytes(), &item); err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		if item.Now.IsZero() {
			return fmt.Errorf("line %d: missing now", line)
		}
		if item.ResetFollow {
			coordinator.ResetFollow()
		}
		if item.ReturnToLeader {
			coordinator.ReturnToLeader()
		}
		if item.Focus != 0 {
			if err := coordinator.Focus(item.Focus); err != nil {
				return fmt.Errorf("line %d: %w", line, err)
			}
		}
		output := result{Focused: coordinator.Focused().Slot, Commands: coordinator.StepWithTerrain(item.Now, item.Positions, item.Terrain)}
		if err := encoder.Encode(output); err != nil {
			return err
		}
	}
	return scanner.Err()
}

func main() {
	path := flag.String("replay", "", "JSONL party frames")
	roster := flag.String("roster", "", "explicit slot:client pairs, e.g. 1:1,2:2")
	leader := flag.Int("leader", 0, "leader slot")
	stop := flag.Float64("stop", -1, "stop distance")
	resume := flag.Float64("resume", -1, "resume distance")
	catchup := flag.Float64("catchup", -1, "catch-up distance")
	maxAge := flag.Duration("max-age", 0, "maximum position age")
	trailSpacing := flag.Float64("trail-spacing", 0, "leader trail sample spacing; requires -trail-max-step")
	trailMaxStep := flag.Float64("trail-max-step", 0, "maximum leader displacement between observations")
	requireTerrain := flag.Bool("require-terrain", false, "stop when an instance-matched terrain grid is unavailable")
	pace := flag.Duration("pace", 0, "delay between replay frames; zero runs immediately")
	lastFocusedLeads := flag.Bool("last-focused-leads", false, "make the manually focused player the follow leader")
	followTargets := flag.String("follow-targets", "", "explicit follower:target pairs, e.g. 2:3")
	flag.Parse()
	players, err := parseRoster(*roster)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	config := follow.Config{StopDistance: *stop, ResumeDistance: *resume, CatchUpDistance: *catchup, MaxAge: *maxAge,
		TrailSpacing: *trailSpacing, TrailMaxStep: *trailMaxStep, RequireTerrain: *requireTerrain}
	coordinator, err := party.New(players, *leader, config)
	if err != nil || *path == "" || *pace < 0 {
		fmt.Fprintln(os.Stderr, "usage: partyreplay -replay file.jsonl -roster 1:1,2:2 -leader 1 -stop N -resume N -catchup N -max-age 750ms")
		os.Exit(2)
	}
	coordinator.SetLastFocusedLeads(*lastFocusedLeads)
	targets, err := parseFollowTargets(*followTargets)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	for slot, target := range targets {
		if err := coordinator.SetFollowTarget(slot, target); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(2)
		}
	}
	file, err := os.Open(*path)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	defer file.Close()
	if err := replay(file, os.Stdout, coordinator, *pace); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
