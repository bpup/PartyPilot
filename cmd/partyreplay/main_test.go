package main

import (
	"bytes"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"partypilot/internal/focus"
	"partypilot/internal/follow"
	"partypilot/internal/navigation"
	"partypilot/internal/party"
)

func TestParseFollowTargetsRejectsAmbiguousRoutes(t *testing.T) {
	targets, err := parseFollowTargets("2:3,4:1")
	if err != nil || len(targets) != 2 || targets[2] != 3 || targets[4] != 1 {
		t.Fatalf("valid target routes: %v, %v", targets, err)
	}
	if targets, err := parseFollowTargets("2:0,3:3,4:7,5:-1"); err != nil ||
		targets[2] != 0 || targets[3] != 3 || targets[4] != 7 || targets[5] != -1 {
		t.Fatalf("current MAIN, self and invalid choices were not parsed: %v %v", targets, err)
	}
	for _, value := range []string{"2:3,2:1", "7:1", "2", "x:1", "2:x", "2:2147483648"} {
		if _, err := parseFollowTargets(value); err == nil {
			t.Fatalf("accepted ambiguous or invalid route %q", value)
		}
	}
}

func TestAsyncPartyReplayAdvancesOnSameTerrain(t *testing.T) {
	at := time.Now().UTC()
	coordinator, err := party.New([]focus.Player{{Slot: 1, Client: 1}, {Slot: 2, Client: 2}}, 1,
		follow.Config{StopDistance: 9, ResumeDistance: 10, CatchUpDistance: 30,
			MaxAge: time.Second, TargetTrail: true, AsyncTargetPath: true})
	if err != nil {
		t.Fatal(err)
	}
	defer coordinator.Close()
	grid := &navigation.Grid{Area: "instance", CellSize: 1, Width: 45,
		Height: 31, TargetNavigation: true}
	for y := 5; y <= 15; y++ {
		grid.Blocked = append(grid.Blocked, navigation.Cell{X: 10, Y: y})
	}
	var input, output bytes.Buffer
	for index := 0; index < 2; index++ {
		now := at.Add(time.Duration(index) * 100 * time.Millisecond)
		item := frame{Now: now, Terrain: grid, Positions: map[int]follow.Position{
			1: {X: 25, Y: 15, Area: "instance", Seen: now, Valid: true},
			2: {X: 5, Y: 15, Area: "instance", Seen: now, Valid: true},
		}}
		if err := json.NewEncoder(&input).Encode(item); err != nil {
			t.Fatal(err)
		}
	}
	if err := replay(&input, &output, coordinator, 50*time.Millisecond); err != nil {
		t.Fatal(err)
	}
	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 2 {
		t.Fatalf("missing replay frames: %s", output.String())
	}
	var first, second result
	if json.Unmarshal([]byte(lines[0]), &first) != nil ||
		json.Unmarshal([]byte(lines[1]), &second) != nil ||
		first.Commands[2].Reason != "Planning route" ||
		second.Commands[2].Reason == "Planning route" || second.Commands[2].Y == 0 {
		t.Fatalf("async party route did not complete: %+v, %+v", first, second)
	}
}

func TestReplayRequiresExplicitResetAfterLeaderJump(t *testing.T) {
	now := time.Unix(40, 0).UTC()
	coordinator, err := party.New([]focus.Player{{Slot: 1, Client: 1}, {Slot: 2, Client: 2}}, 1,
		follow.Config{StopDistance: 1, ResumeDistance: 2, CatchUpDistance: 30,
			MaxAge: time.Second, TrailSpacing: 1, TrailMaxStep: 5})
	if err != nil {
		t.Fatal(err)
	}
	var input, output bytes.Buffer
	for index, leaderX := range []float64{0, 20, 20} {
		at := now.Add(time.Duration(index) * 100 * time.Millisecond)
		item := frame{Now: at, ResetFollow: index == 2, Positions: map[int]follow.Position{
			1: {X: leaderX, Area: "instance", Seen: at, Valid: true},
			2: {X: -10, Area: "instance", Seen: at, Valid: true},
		}}
		if err := json.NewEncoder(&input).Encode(item); err != nil {
			t.Fatal(err)
		}
	}
	if err := replay(&input, &output, coordinator, 0); err != nil {
		t.Fatal(err)
	}
	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 3 {
		t.Fatalf("unexpected output: %s", output.String())
	}
	for index, expected := range []follow.State{follow.Following, follow.Waiting, follow.CatchingUp} {
		var got result
		if err := json.Unmarshal([]byte(lines[index]), &got); err != nil {
			t.Fatal(err)
		}
		if got.Commands[2].State != expected {
			t.Fatalf("frame %d: got %+v, want %s", index, got.Commands[2], expected)
		}
	}
}
