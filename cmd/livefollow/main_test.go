package main

import (
	"os"
	"path/filepath"
	"testing"
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
