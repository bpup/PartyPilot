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
	shared := coordinator.Step(now, positions)
	if coordinator.Focused().Slot != 2 || shared[1].X <= 0 {
		t.Fatalf("shared focus forgot the last manual P2 MAIN: %+v", shared)
	}
	if err := coordinator.Focus(1); err != nil {
		t.Fatal(err)
	}
	restored := coordinator.Step(now, positions)
	if _, hasMainCommand := restored[1]; hasMainCommand || restored[2].X >= 0 {
		t.Fatalf("manual P1 focus did not restore P1 MAIN: %+v", restored)
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
	for _, target := range []int{2, 6, 7, -1} {
		if err := coordinator.SetFollowTarget(2, target); err != nil {
			t.Fatal(err)
		}
		if command := coordinator.Step(now, positions)[2]; command.X >= 0 {
			t.Fatalf("self or inactive target did not return to current MAIN: target=%d command=%+v", target, command)
		}
	}
	if coordinator.SetFollowTarget(6, 1) == nil {
		t.Fatal("inactive follower accepted")
	}
	coordinator.SetLastFocusedLeads(true)
	if err := coordinator.Focus(3); err != nil {
		t.Fatal(err)
	}
	if command := coordinator.Step(now, positions)[2]; command.X <= 0 {
		t.Fatalf("normalized current MAIN choice did not follow the new MAIN: %+v", command)
	}
}

func TestSixPlayerFollowGraphResolvesCyclesToCurrentMain(t *testing.T) {
	now := time.Unix(31, 0)
	players := []focus.Player{
		{Slot: 1, Client: 1}, {Slot: 2, Client: 1},
		{Slot: 3, Client: 2}, {Slot: 4, Client: 2},
		{Slot: 5, Client: 3}, {Slot: 6, Client: 3},
	}
	coordinator, err := New(players, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3,
			CatchUpDistance: 10, MaxAge: time.Second})
	if err != nil {
		t.Fatal(err)
	}
	for slot, target := range map[int]int{2: 3, 3: 2, 4: 2, 5: 6} {
		if err := coordinator.SetFollowTarget(slot, target); err != nil {
			t.Fatal(err)
		}
	}
	positions := map[int]follow.Position{}
	for slot, x := range map[int]float64{1: 0, 2: 10, 3: 20, 4: 5, 5: 50, 6: 60} {
		positions[slot] = follow.Position{X: x, Area: "verified-instance", Seen: now, Valid: true}
	}
	commands := coordinator.Step(now, positions)
	if commands[2].X >= 0 || commands[3].X >= 0 ||
		commands[4].X <= 0 || commands[5].X <= 0 || commands[6].X >= 0 {
		t.Fatalf("cycle or independent chains were routed incorrectly: %+v", commands)
	}
	coordinator.SetLastFocusedLeads(true)
	if err := coordinator.Focus(3); err != nil {
		t.Fatal(err)
	}
	commands = coordinator.Step(now, positions)
	if _, hasMainCommand := commands[3]; hasMainCommand ||
		commands[1].X <= 0 || commands[2].X <= 0 || commands[4].X <= 0 {
		t.Fatalf("new MAIN did not resolve the follow graph: %+v", commands)
	}
}
