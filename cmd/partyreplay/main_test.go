package main

import (
	"bytes"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"partypilot/internal/focus"
	"partypilot/internal/follow"
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
