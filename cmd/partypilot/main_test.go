package main

import (
	"bytes"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"partypilot/internal/follow"
)

func TestPackedTerrainReplayProducesDetour(t *testing.T) {
	// 0x10 in rows 1..3 blocks x=2; base64 encoding of 15 bytes.
	input := `{"now":"2026-10-26T00:00:00Z","leader":{"area":"a","x":15.5,"y":22.5,"seen":"2026-10-26T00:00:00Z","valid":true},"follower":{"area":"a","x":10.5,"y":22.5,"seen":"2026-10-26T00:00:00Z","valid":true},"packed_terrain":{"area":"a","origin_x":10,"origin_y":20,"cell_size":1,"bytes_per_row":3,"rows":5,"data":"ERERERARERARERARERER"}}` + "\n"
	second := strings.ReplaceAll(input, "2026-10-26T00:00:00Z", "2026-10-26T00:00:00.1Z")
	second = strings.Replace(second, `"x":10.5`, `"x":11.5`, 1)
	var output bytes.Buffer
	config := follow.Config{StopDistance: 1, ResumeDistance: 2, CatchUpDistance: 10, MaxAge: time.Second}
	if err := replay(strings.NewReader(input+second), &output, config); err != nil {
		t.Fatal(err)
	}
	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 2 {
		t.Fatalf("missing route steps: %s", output.String())
	}
	var command follow.Command
	if err := json.Unmarshal([]byte(lines[1]), &command); err != nil ||
		command.State != follow.Following || command.X != 0 || command.Y != -1 {
		t.Fatalf("packed wall did not cause expected turn: %+v, %v", command, err)
	}
}

func TestPackedTerrainReplayRejectsAmbiguousOrIncompleteInput(t *testing.T) {
	for _, input := range []string{
		`{"now":"2026-10-26T00:00:00Z","terrain":{},"packed_terrain":{}}`,
		`{"now":"2026-10-26T00:00:00Z","packed_terrain":{"area":"a","cell_size":1,"bytes_per_row":1,"rows":2,"data":"EQ=="}}`,
	} {
		if err := replay(strings.NewReader(input+"\n"), &bytes.Buffer{}, follow.Config{
			StopDistance: 1, ResumeDistance: 2, CatchUpDistance: 10, MaxAge: time.Second,
		}); err == nil {
			t.Fatalf("accepted invalid packed replay: %s", input)
		}
	}
}
