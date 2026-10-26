package main

import (
	"path/filepath"
	"testing"
)

func TestLogFlagsRejectDuplicateAndPreserveWindowsPath(t *testing.T) {
	var logs logFlags
	if err := logs.Set(`1=C:\Games\Path of Exile 2\logs\Client.txt`); err != nil || logs[1] != `C:\Games\Path of Exile 2\logs\Client.txt` {
		t.Fatalf("Windows path failed: %v, %q", err, logs[1])
	}
	for _, value := range []string{`1=other.txt`, `0=bad`, `7=bad`, `2`, `2=`} {
		if err := logs.Set(value); err == nil {
			t.Fatalf("accepted invalid -log %q", value)
		}
	}
}

func TestResolveExeLogs(t *testing.T) {
	paths, err := resolveExeLogs(logFlags{1: filepath.Join("games", "a", "PathOfExile.exe")}, logFlags{2: filepath.Join("games", "b", "PathOfExile.exe")})
	if err != nil || len(paths) != 2 || paths[2] != filepath.Join("games", "b", "logs", "Client.txt") {
		t.Fatalf("could not resolve paths: %v, %v", paths, err)
	}
	if _, err := resolveExeLogs(logFlags{1: "a"}, logFlags{1: "b"}); err == nil {
		t.Fatal("accepted two sources for one slot")
	}
}
