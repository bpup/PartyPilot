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

func TestLastFocusedLeadsReassignsFormerLeader(t *testing.T) {
	now := time.Unix(20, 0)
	coordinator, err := New(
		[]focus.Player{{Slot: 1, Client: 1}, {Slot: 2, Client: 2}, {Slot: 3, Client: 2}},
		1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second},
	)
	if err != nil {
		t.Fatal(err)
	}
	coordinator.SetLastFocusedLeads(true)
	positions := map[int]follow.Position{
		1: {X: 0, Area: "verified-instance", Seen: now, Valid: true},
		2: {X: 20, Area: "verified-instance", Seen: now, Valid: true},
		3: {X: 1, Area: "verified-instance", Seen: now, Valid: true},
	}
	initial := coordinator.Step(now, positions)
	if _, hasMainCommand := initial[1]; hasMainCommand || initial[2].X >= 0 {
		t.Fatalf("initial leader routing: %+v", initial)
	}
	if err := coordinator.Focus(2); err != nil {
		t.Fatal(err)
	}
	switched := coordinator.Step(now, positions)
	if _, hasFocusedCommand := switched[2]; hasFocusedCommand ||
		switched[1].State != follow.CatchingUp || switched[1].X <= 0 ||
		switched[3].State != follow.CatchingUp || switched[3].X <= 0 {
		t.Fatalf("focus did not make P2 leader: %+v", switched)
	}
	coordinator.ReturnToLeader()
	restored := coordinator.Step(now, positions)
	if _, hasMainCommand := restored[1]; hasMainCommand || restored[2].X >= 0 {
		t.Fatalf("return to P1 did not restore routing: %+v", restored)
	}
}

func TestSpecificFollowTargetAndReturnToCurrentMain(t *testing.T) {
	now := time.Unix(30, 0)
	coordinator, err := New(
		[]focus.Player{{Slot: 1, Client: 1}, {Slot: 2, Client: 2}, {Slot: 3, Client: 2}},
		1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second},
	)
	if err != nil {
		t.Fatal(err)
	}
	positions := map[int]follow.Position{
		1: {X: 0, Area: "verified-instance", Seen: now, Valid: true},
		2: {X: 20, Area: "verified-instance", Seen: now, Valid: true},
		3: {X: 30, Area: "verified-instance", Seen: now, Valid: true},
	}
	if command := coordinator.Step(now, positions)[2]; command.X >= 0 {
		t.Fatalf("default current MAIN target: %+v", command)
	}
	if err := coordinator.SetFollowTarget(2, 3); err != nil {
		t.Fatal(err)
	}
	if command := coordinator.Step(now, positions)[2]; command.X <= 0 || command.State != follow.CatchingUp {
		t.Fatalf("specific target did not take effect: %+v", command)
	}
	if err := coordinator.SetFollowTarget(2, 0); err != nil {
		t.Fatal(err)
	}
	if command := coordinator.Step(now, positions)[2]; command.X >= 0 {
		t.Fatalf("current MAIN target was not restored: %+v", command)
	}
	if coordinator.SetFollowTarget(2, 2) == nil || coordinator.SetFollowTarget(2, 6) == nil {
		t.Fatal("invalid follow target accepted")
	}
}
