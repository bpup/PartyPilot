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
	packed := make([]byte, 10*15)
	for i := range packed {
		packed[i] = 0x11
	}
	for y := 4; y <= 10; y++ {
		packed[y*10+5] = 0x10
	}
	at := time.Date(2026, 10, 26, 0, 0, 0, 0, time.UTC)
	item := frame{Now: at,
		Leader:        follow.Position{X: 16, Y: 7, Area: "a", Seen: at, Valid: true},
		Follower:      follow.Position{X: 4, Y: 7, Area: "a", Seen: at, Valid: true},
		PackedTerrain: &packedTerrain{Area: "a", CellSize: 1, BytesPerRow: 10, Rows: 15, Data: packed}}
	first, _ := json.Marshal(item)
	item.Now = at.Add(100 * time.Millisecond)
	item.Leader.Seen, item.Follower.Seen = item.Now, item.Now
	item.Follower.X = 5
	second, _ := json.Marshal(item)
	var output bytes.Buffer
	config := follow.Config{StopDistance: 1, ResumeDistance: 2, CatchUpDistance: 30, MaxAge: time.Second}
	if err := replay(strings.NewReader(string(first)+"\n"+string(second)+"\n"), &output, config); err != nil {
		t.Fatal(err)
	}
	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 2 {
		t.Fatalf("missing route steps: %s", output.String())
	}
	var command follow.Command
	if err := json.Unmarshal([]byte(lines[1]), &command); err != nil ||
		command.State != follow.Following || command.Y == 0 {
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
