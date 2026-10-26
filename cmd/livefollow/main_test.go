package main

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

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

func TestMenuInputRequiresBoundPlayerAndOpenStream(t *testing.T) {
	session, err := live.NewFlaskSession(map[int]int{1: 101, 2: 202}, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	input := make(chan menuInput, 2)
	input <- menuInput{Type: "menu_guard", Slot: 1, State: "clear"}
	if err := applyMenuInput(session, input); err != nil {
		t.Fatal(err)
	}
	input <- menuInput{Type: "menu_guard", Slot: 3, State: "clear"}
	if err := applyMenuInput(session, input); err == nil {
		t.Fatal("unbound player menu command accepted")
	}
	close(input)
	if err := applyMenuInput(session, input); err == nil {
		t.Fatal("closed menu input left automatic flasks armed")
	}
	malformed := make(chan menuInput, 2)
	readMenuInput(strings.NewReader("{\"type\":\"menu_guard\",\"slot\":1,\"state\":\"clear\"}\n{}\n"), malformed)
	first := <-malformed
	second := <-malformed
	if first.Err != nil || first.Slot != 1 || second.Err == nil {
		t.Fatalf("menu input parsing failed: %+v %+v", first, second)
	}
}
