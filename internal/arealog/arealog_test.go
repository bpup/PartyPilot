package arealog

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestGenerationAndFreshComparison(t *testing.T) {
	now := time.Now().UTC()
	line := `2026/10/26 Generating level 68 area "G2_4" with seed 12345`
	leader, ok := ParseGeneration(line, 1, now)
	if !ok || leader.AreaID != "G2_4" || leader.Seed != 12345 {
		t.Fatalf("unexpected leader signal: %+v, %t", leader, ok)
	}
	follower, ok := ParseGeneration(strings.Replace(line, "G2_4", "g2_4", 1), 2, now)
	if !ok || Compare(now, time.Second, 1, 2, &leader, &follower).Status != "candidate_same_instance" {
		t.Fatal("fresh equal area and seed should be a candidate")
	}
	follower.Seed++
	if Compare(now, time.Second, 1, 2, &leader, &follower).Status != "different_area" {
		t.Fatal("distinct seeds must not match")
	}
	follower.Seed--
	if Compare(now.Add(2*time.Second), time.Second, 1, 2, &leader, &follower).Status != "stale" {
		t.Fatal("old signals must expire")
	}
	if Compare(now, time.Second, 1, 2, nil, &follower).Status != "missing" {
		t.Fatal("missing leader must remain missing")
	}
	follower.Origin = "history"
	if Compare(now, time.Second, 1, 2, &leader, &follower).Status != "historical_candidate" {
		t.Fatal("primed history must not appear as a live candidate")
	}
	for _, invalid := range []string{
		`[SCENE] Set Source [G2_4]`,
		`Generating level 68 area "G2_4" with seed 0`,
		`Generating level 68 area "G2_4" with seed 999999999999999999999999`,
	} {
		if _, ok := ParseGeneration(invalid, 1, now); ok {
			t.Fatalf("accepted incomplete identity: %q", invalid)
		}
	}
}

func TestPrimeOnlyCompleteHistoryFromBoundedSuffix(t *testing.T) {
	path := filepath.Join(t.TempDir(), "Client.txt")
	content := strings.Repeat("x", int(primeBytes)+10) + "\n" +
		"Generating level 1 area \"OLD\" with seed 1\n" +
		"Generating level 68 area \"CURRENT\" with seed 2\n" +
		"Generating level 69 area \"INCOMPLETE\" with seed 3"
	if err := os.WriteFile(path, []byte(content), 0600); err != nil {
		t.Fatal(err)
	}
	signal, err := Prime(path, 2, time.Now().UTC())
	if err != nil || signal == nil || signal.AreaID != "CURRENT" || signal.Seed != 2 || signal.Origin != "history" {
		t.Fatalf("wrong primed signal: %+v, %v", signal, err)
	}
}

func TestDistinctLogPaths(t *testing.T) {
	directory := t.TempDir()
	first := filepath.Join(directory, "one.txt")
	second := filepath.Join(directory, "two.txt")
	for _, path := range []string{first, second} {
		if err := os.WriteFile(path, nil, 0600); err != nil {
			t.Fatal(err)
		}
	}
	if err := ValidatePaths(map[int]string{1: first, 2: second}); err != nil {
		t.Fatal(err)
	}
	if err := ValidateClientPaths(map[int]string{1: first}); err != nil {
		t.Fatalf("one dual-local client log was rejected: %v", err)
	}
	if err := ValidateClientPaths(map[int]string{1: first, 2: second}); err != nil {
		t.Fatal(err)
	}
	if err := ValidatePaths(map[int]string{1: first, 2: first}); err == nil {
		t.Fatal("accepted shared path")
	}
	link := filepath.Join(directory, "same.txt")
	if err := os.Symlink(first, link); err != nil {
		t.Fatal(err)
	}
	if err := ValidatePaths(map[int]string{1: first, 2: link}); err == nil {
		t.Fatal("accepted alias to same log")
	}
	if err := ValidateClientPaths(map[int]string{1: first, 2: link}); err == nil {
		t.Fatal("ordered clients shared a log")
	}
}

func TestTailIgnoresHistoryAndWaitsForCompleteLine(t *testing.T) {
	path := filepath.Join(t.TempDir(), "Client.txt")
	if err := os.WriteFile(path, []byte("old line\n"), 0600); err != nil {
		t.Fatal(err)
	}
	tail, err := OpenTail(path)
	if err != nil {
		t.Fatal(err)
	}
	defer tail.Close()
	if lines, err := tail.Poll(); err != nil || len(lines) != 0 {
		t.Fatalf("history became fresh: %q, %v", lines, err)
	}
	appendText := func(value string) {
		t.Helper()
		file, err := os.OpenFile(path, os.O_APPEND|os.O_WRONLY, 0600)
		if err != nil {
			t.Fatal(err)
		}
		_, err = file.WriteString(value)
		file.Close()
		if err != nil {
			t.Fatal(err)
		}
	}
	appendText("new")
	if lines, err := tail.Poll(); err != nil || len(lines) != 0 {
		t.Fatalf("partial line emitted: %q, %v", lines, err)
	}
	appendText(" line\r\n")
	if lines, err := tail.Poll(); err != nil || len(lines) != 1 || lines[0] != "new line" {
		t.Fatalf("complete line missing: %q, %v", lines, err)
	}
}

func TestTailReopensRotatedLog(t *testing.T) {
	path := filepath.Join(t.TempDir(), "Client.txt")
	if err := os.WriteFile(path, []byte("old\n"), 0600); err != nil {
		t.Fatal(err)
	}
	tail, err := OpenTail(path)
	if err != nil {
		t.Fatal(err)
	}
	defer tail.Close()
	if err := os.Rename(path, path+".1"); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte("new\n"), 0600); err != nil {
		t.Fatal(err)
	}
	lines, err := tail.Poll()
	if err != nil || len(lines) != 1 || lines[0] != "new" {
		t.Fatalf("rotation did not reopen new log: %q, %v", lines, err)
	}
}
