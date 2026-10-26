package main

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
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

func TestPackedTerrainReplayReadsRawFileAndRejectsTwoSources(t *testing.T) {
	data := bytes.Repeat([]byte{0x11}, 10*15)
	for y := 4; y <= 10; y++ {
		data[y*10+5] = 0x10
	}
	path := filepath.Join(t.TempDir(), "terrain.bin")
	if err := os.WriteFile(path, data, 0600); err != nil {
		t.Fatal(err)
	}
	at := time.Date(2026, 10, 26, 0, 0, 0, 0, time.UTC)
	item := frame{Now: at,
		Leader:        follow.Position{X: 16, Y: 7, Area: "a", Seen: at, Valid: true},
		Follower:      follow.Position{X: 4, Y: 7, Area: "a", Seen: at, Valid: true},
		PackedTerrain: &packedTerrain{Area: "a", CellSize: 1, BytesPerRow: 10, Rows: 15, DataFile: path}}
	encoded, _ := json.Marshal(item)
	config := follow.Config{StopDistance: 1, ResumeDistance: 2, CatchUpDistance: 30, MaxAge: time.Second}
	var output bytes.Buffer
	if err := replay(bytes.NewReader(append(encoded, '\n')), &output, config); err != nil {
		t.Fatal(err)
	}
	var command follow.Command
	if err := json.Unmarshal(output.Bytes(), &command); err != nil ||
		command.State != follow.Following || command.Y == 0 {
		t.Fatalf("raw terrain file was not used for detour: %+v, %v", command, err)
	}
	item.PackedTerrain.Data = []byte{0x11}
	encoded, _ = json.Marshal(item)
	if err := replay(bytes.NewReader(append(encoded, '\n')), &bytes.Buffer{}, config); err == nil {
		t.Fatal("accepted both base64 data and raw terrain file")
	}
	item.PackedTerrain.Data = nil
	item.PackedTerrain.Rows++
	encoded, _ = json.Marshal(item)
	if err := replay(bytes.NewReader(append(encoded, '\n')), &bytes.Buffer{}, config); err == nil {
		t.Fatal("accepted terrain file shorter than declared dimensions")
	}
}

func TestPackedTerrainReplayLoadsValidatedDiagnosticCapture(t *testing.T) {
	data := bytes.Repeat([]byte{0x11}, 46*92)
	for y := 4; y <= 10; y++ {
		data[y*46+5] = 0x10
	}
	directory := t.TempDir()
	dataPath := filepath.Join(directory, "terrain.packed")
	metaPath := filepath.Join(directory, "terrain.json")
	if err := os.WriteFile(dataPath, data, 0600); err != nil {
		t.Fatal(err)
	}
	digest := sha256.Sum256(data)
	meta := map[string]any{
		"Status": "candidate_capture_only", "ProcessId": 123,
		"AreaHash": "77", "AreaAddress": "0x100000", "Anchor": "ingame_data_address",
		"PackedStart": "0x200000", "RawGridX": 4, "RawGridY": 7,
		"Width": 92, "Height": 92, "BytesPerRow": 46, "PackedBytes": len(data),
		"PatchX": 4, "PatchY": 7, "PatchRows": []string{"111"},
		"Sha256": hex.EncodeToString(digest[:]), "DataFile": "terrain.packed",
	}
	encodedMeta, _ := json.Marshal(meta)
	if err := os.WriteFile(metaPath, encodedMeta, 0600); err != nil {
		t.Fatal(err)
	}
	at := time.Date(2026, 10, 26, 0, 0, 0, 0, time.UTC)
	item := frame{Now: at,
		Leader:        follow.Position{X: 16, Y: 7, Area: "a", Seen: at, Valid: true},
		Follower:      follow.Position{X: 4, Y: 7, Area: "a", Seen: at, Valid: true},
		PackedTerrain: &packedTerrain{Area: "a", CellSize: 1, CaptureFile: metaPath}}
	encodedFrame, _ := json.Marshal(item)
	config := follow.Config{StopDistance: 1, ResumeDistance: 2,
		CatchUpDistance: 30, MaxAge: time.Second}
	var output bytes.Buffer
	if err := replay(bytes.NewReader(append(encodedFrame, '\n')), &output, config); err != nil {
		t.Fatal(err)
	}
	var command follow.Command
	if err := json.Unmarshal(output.Bytes(), &command); err != nil ||
		command.State != follow.Following || command.Y == 0 {
		t.Fatalf("validated capture did not detour: %+v, %v", command, err)
	}
	item.PackedTerrain.BytesPerRow = 46
	encodedFrame, _ = json.Marshal(item)
	if err := replay(bytes.NewReader(append(encodedFrame, '\n')), &bytes.Buffer{}, config); err == nil {
		t.Fatal("capture and manual dimensions were both accepted")
	}
}
