package main

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
	"partypilot/internal/live"
)

func TestSourcesAndRoster(t *testing.T) {
	roster, err := parseRoster("1:101,2:202")
	if err != nil || len(roster) != 2 {
		t.Fatalf("valid roster rejected: %v", err)
	}
	for _, value := range []string{"", "1:101", "1:101,1:202", "1:101,2:101,3:303,4:404", "1:101,2:0"} {
		if _, err := parseRoster(value); err == nil {
			t.Fatalf("accepted bad roster %q", value)
		}
	}
	directory := t.TempDir()
	first := filepath.Join(directory, "one.txt")
	second := filepath.Join(directory, "two.txt")
	for _, path := range []string{first, second} {
		if err := os.WriteFile(path, nil, 0600); err != nil {
			t.Fatal(err)
		}
	}
	paths, err := resolveLogs(roster, sources{1: first, 2: second}, nil)
	if err != nil || len(paths) != 2 {
		t.Fatalf("valid logs rejected: %v", err)
	}
	if _, err := resolveLogs(roster, sources{1: first, 2: first}, nil); err == nil {
		t.Fatal("shared log accepted")
	}
	if _, err := resolveLogs(roster, sources{1: first}, nil); err == nil {
		t.Fatal("missing log accepted")
	}
}

func TestClassicCollisionDodgeRequiresAllBoundTerrain(t *testing.T) {
	roster := map[int]int{1: 101, 2: 202}
	if err := validateClassicCollisionBindings(false, roster, nil); err != nil {
		t.Fatalf("disabled target-default collision mode required terrain: %v", err)
	}
	if err := validateClassicCollisionBindings(true, roster, nil); err == nil {
		t.Fatal("collision mode accepted missing terrain bindings")
	}
	if err := validateClassicCollisionBindings(true, roster, sources{1: "first.json"}); err == nil {
		t.Fatal("collision mode accepted a partially bound party")
	}
	if err := validateClassicCollisionBindings(true, roster,
		sources{1: "first.json", 2: "second.json"}); err != nil {
		t.Fatalf("complete explicit terrain bindings were rejected: %v", err)
	}
	if err := validateClassicCollisionBindings(true, roster,
		sources{1: "first.json", 3: "third.json"}); err == nil {
		t.Fatal("collision mode accepted terrain for the wrong client")
	}
}

func TestOrderedRoleBindingsUseClientLogs(t *testing.T) {
	var bindings roleBindings
	for _, value := range []string{"1:1:1:101", "2:1:2:101", "3:2:1:202", "4:2:2:202"} {
		if err := bindings.Set(value); err != nil {
			t.Fatal(err)
		}
	}
	for _, value := range []string{"1:2:3", "a:1:1:101", "1:1:1:0"} {
		if err := bindings.Set(value); err == nil {
			t.Fatalf("invalid role binding accepted: %q", value)
		}
	}
	clients, err := clientRoster(bindings)
	if err != nil || len(clients) != 2 || clients[1] != 101 || clients[2] != 202 {
		t.Fatalf("role clients were not preserved: %+v %v", clients, err)
	}
	directory := t.TempDir()
	first := filepath.Join(directory, "one.txt")
	second := filepath.Join(directory, "two.txt")
	for _, path := range []string{first, second} {
		if err := os.WriteFile(path, nil, 0600); err != nil {
			t.Fatal(err)
		}
	}
	paths, err := resolveLogPaths(clients, sources{1: first, 2: second}, nil,
		arealog.ValidateClientPaths)
	if err != nil || len(paths) != 2 {
		t.Fatalf("client logs were rejected: %+v %v", paths, err)
	}
	if _, err := resolveLogPaths(clients, sources{1: first, 2: first}, nil,
		arealog.ValidateClientPaths); err == nil {
		t.Fatal("two clients shared one log")
	}
	if got, err := clientRoster([]live.RoleBinding{{Slot: 1, Client: 1, LocalPlayer: 1, ProcessID: 101},
		{Slot: 2, Client: 1, LocalPlayer: 2, ProcessID: 202}}); err == nil || got != nil {
		t.Fatal("one client mapped to two processes")
	}
}

func TestMemberNamesRequireExplicitKnownSlots(t *testing.T) {
	var names memberNames
	if err := names.Set("2=Second"); err != nil || names[2] != "Second" {
		t.Fatalf("valid member name rejected: %v", err)
	}
	for _, value := range []string{"2=Duplicate", "3=", "0=Nope", "bad=Nope"} {
		if err := names.Set(value); err == nil {
			t.Fatalf("invalid member name accepted: %q", value)
		}
	}
}

func TestFollowTargetArguments(t *testing.T) {
	var targets followTargets
	for _, value := range []string{"2=3", "3=0", "1=7"} {
		if err := targets.Set(value); err != nil {
			t.Fatal(err)
		}
	}
	if targets[2] != 3 || targets[3] != 0 || targets[1] != 7 {
		t.Fatalf("target choices changed: %+v", targets)
	}
	for _, value := range []string{"2=1", "7=1", "1", "1=bad", "1=2147483648"} {
		if err := targets.Set(value); err == nil {
			t.Fatalf("accepted ambiguous or invalid target %q", value)
		}
	}
}

func TestIndividualStopArguments(t *testing.T) {
	var distances stopDistances
	if err := distances.Set("2=37.25"); err != nil || distances[2] != 37.25 {
		t.Fatalf("valid player stop rejected: %+v %v", distances, err)
	}
	for _, value := range []string{"2=38", "7=10", "1", "1=NaN", "1=Inf", "1=-1", "1=201"} {
		if err := distances.Set(value); err == nil {
			t.Fatalf("invalid individual stop accepted: %q", value)
		}
	}
}

func TestMenuInputRequiresBoundPlayerAndOpenStream(t *testing.T) {
	session, err := live.NewFlaskSession(map[int]int{1: 101, 2: 202}, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	engine, err := live.New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	input := make(chan menuInput, 2)
	input <- menuInput{Type: "menu_guard", Slot: 1, State: "clear"}
	if err := applyMenuInput(session, engine, input, false); err != nil {
		t.Fatal(err)
	}
	input <- menuInput{Type: "menu_guard", Slot: 3, State: "clear"}
	if err := applyMenuInput(session, engine, input, false); err == nil {
		t.Fatal("unbound player menu command accepted")
	}
	close(input)
	if err := applyMenuInput(session, engine, input, false); err == nil {
		t.Fatal("closed menu input left automatic flasks armed")
	}
	malformed := make(chan menuInput, 2)
	readMenuInput(strings.NewReader("{\"type\":\"menu_guard\",\"slot\":1,\"state\":\"clear\"}\n{}\n"), malformed)
	first := <-malformed
	second := <-malformed
	if first.Err != nil || first.Slot != 1 || second.Err == nil {
		t.Fatalf("menu input parsing failed: %+v %+v", first, second)
	}
	engine.SetLastFocusedLeads(true)
	focus := make(chan menuInput, 2)
	focus <- menuInput{Type: "manual_focus", Slot: 2}
	if err := applyMenuInput(nil, engine, focus, false); err != nil || engine.Leader() != 2 {
		t.Fatalf("manual focus did not change live MAIN: %v", err)
	}
	focus <- menuInput{Type: "manual_focus", Slot: 3}
	if err := applyMenuInput(nil, engine, focus, false); err == nil || engine.Leader() != 2 {
		t.Fatal("unknown manual focus changed live MAIN")
	}
}

func TestFollowPauseControlRequiresOptInAndExplicitState(t *testing.T) {
	engine, err := live.New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10,
			MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	input := make(chan menuInput, 1)
	input <- menuInput{Type: "follow_pause", State: "paused"}
	if err := applyMenuInput(nil, engine, input, false); err == nil || engine.FollowPaused() {
		t.Fatal("follow pause bypassed explicit opt-in")
	}
	input <- menuInput{Type: "follow_pause", State: "paused"}
	if err := applyMenuInput(nil, engine, input, true); err != nil || !engine.FollowPaused() {
		t.Fatalf("explicit pause was lost: %v", err)
	}
	input <- menuInput{Type: "follow_pause", State: "unexpected"}
	if err := applyMenuInput(nil, engine, input, true); err == nil || !engine.FollowPaused() {
		t.Fatal("invalid pause state changed follow")
	}
	input <- menuInput{Type: "follow_pause", State: "following"}
	if err := applyMenuInput(nil, engine, input, true); err != nil || engine.FollowPaused() {
		t.Fatalf("explicit resume was lost: %v", err)
	}
}

func TestMainInputControlRequiresOptInAndCurrentLeader(t *testing.T) {
	engine, err := live.New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10,
			MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	at := time.Now().UTC()
	input := make(chan menuInput, 1)
	control := menuInput{Type: "main_input", Slot: 1, LeftX: 0.5,
		LeftY: -0.25, BDown: true, ObservedAtMS: at.UnixMilli()}
	input <- control
	if err := applyMenuInput(nil, engine, input, false); err == nil {
		t.Fatal("main controller sample bypassed opt-in")
	}
	engine.SetAcceptMainInput(true)
	input <- control
	if err := applyMenuInput(nil, engine, input, false); err != nil {
		t.Fatalf("bound main controller sample was rejected: %v", err)
	}
	if got, ok := engine.CurrentMainInput(time.Now().UTC()); !ok ||
		got.Slot != 1 || got.LeftX != 0.5 || !got.BDown {
		t.Fatalf("accepted input was not retained: %+v %v", got, ok)
	}
	control.Slot = 2
	input <- control
	if err := applyMenuInput(nil, engine, input, false); err != nil {
		t.Fatalf("late sample from another role stopped following: %v", err)
	}
	if got, ok := engine.CurrentMainInput(time.Now().UTC()); !ok || got.Slot != 1 {
		t.Fatalf("other role replaced the main sample: %+v %v", got, ok)
	}
}

func TestMirrorBPlayerSelectionMatchesRoster(t *testing.T) {
	roster := map[int]int{1: 101, 2: 202}
	if allowed, err := parseMirrorBPlayers("2", roster); err != nil || !allowed[2] || allowed[1] {
		t.Fatalf("per-player B choice was not retained: %+v %v", allowed, err)
	}
	if allowed, err := parseMirrorBPlayers("none", roster); err != nil || len(allowed) != 0 {
		t.Fatalf("B mirror could not be disabled: %+v %v", allowed, err)
	}
	for _, value := range []string{"", "1,1", "1,3", "bad", "1,"} {
		if _, err := parseMirrorBPlayers(value, roster); err == nil {
			t.Fatalf("invalid B mirror players accepted: %q", value)
		}
	}
}
