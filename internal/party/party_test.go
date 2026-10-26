package party

import (
	"testing"
	"time"

	"partypilot/internal/focus"
	"partypilot/internal/follow"
)

func TestManualFocusStopsOnlySelectedFollower(t *testing.T) {
	now := time.Unix(10, 0)
	coordinator, err := New(
		[]focus.Player{{Slot: 1, Client: 1}, {Slot: 2, Client: 2}, {Slot: 3, Client: 2}},
		1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second},
	)
	if err != nil {
		t.Fatal(err)
	}
	positions := map[int]follow.Position{
		1: {X: 20, Area: "verified-instance", Seen: now, Valid: true},
		2: {X: 0, Area: "verified-instance", Seen: now, Valid: true},
		3: {X: 1, Area: "verified-instance", Seen: now, Valid: true},
	}
	commands := coordinator.Step(now, positions)
	if commands[2].State != follow.CatchingUp || commands[3].State != follow.CatchingUp {
		t.Fatalf("initial commands: %+v", commands)
	}
	if err := coordinator.Focus(2); err != nil {
		t.Fatal(err)
	}
	commands = coordinator.Step(now, positions)
	if commands[2].State != follow.Holding || commands[2].Reason != "manual control" ||
		commands[3].State != follow.CatchingUp {
		t.Fatalf("manual focus: %+v", commands)
	}
	coordinator.ReturnToLeader()
	commands = coordinator.Step(now, positions)
	if commands[2].State != follow.CatchingUp {
		t.Fatalf("resume: %+v", commands)
	}
}

func TestUnknownInstanceStopsAllFollowers(t *testing.T) {
	now := time.Unix(10, 0)
	coordinator, err := New(
		[]focus.Player{{Slot: 1, Client: 1}, {Slot: 2, Client: 2}}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second},
	)
	if err != nil {
		t.Fatal(err)
	}
	commands := coordinator.Step(now, map[int]follow.Position{
		1: {X: 20, Seen: now, Valid: true},
		2: {Seen: now, Valid: true},
	})
	if commands[2].State != follow.Waiting {
		t.Fatalf("unknown instance moved: %+v", commands[2])
	}
}
