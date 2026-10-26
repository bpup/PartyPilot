package main

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

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
	if err := applyMenuInput(session, engine, input); err != nil {
		t.Fatal(err)
	}
	input <- menuInput{Type: "menu_guard", Slot: 3, State: "clear"}
	if err := applyMenuInput(session, engine, input); err == nil {
		t.Fatal("unbound player menu command accepted")
	}
	close(input)
	if err := applyMenuInput(session, engine, input); err == nil {
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
	if err := applyMenuInput(nil, engine, focus); err != nil || engine.Leader() != 2 {
		t.Fatalf("manual focus did not change live MAIN: %v", err)
	}
	focus <- menuInput{Type: "manual_focus", Slot: 3}
	if err := applyMenuInput(nil, engine, focus); err == nil || engine.Leader() != 2 {
		t.Fatal("unknown manual focus changed live MAIN")
	}
}
