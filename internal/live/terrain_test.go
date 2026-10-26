package live

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
	"partypilot/internal/telemetry"
)

func testTerrainCapture(t *testing.T, seen time.Time) string {
	t.Helper()
	dir := t.TempDir()
	data := bytes.Repeat([]byte{0x11}, 64*64)
	if err := os.WriteFile(filepath.Join(dir, "map.packed"), data, 0600); err != nil {
		t.Fatal(err)
	}
	sum := sha256.Sum256(data)
	meta := map[string]any{"Status": "candidate_capture_only", "ProcessId": 202,
		"AreaHash": "77", "AreaAddress": "0x100000", "Anchor": "ingame_data_address",
		"PackedStart": "0x200000", "RawGridX": 10, "RawGridY": 20,
		"Width": 128, "Height": 64, "BytesPerRow": 64, "PackedBytes": len(data),
		"PatchX": 10, "PatchY": 20, "PatchRows": []string{"11"},
		"Sha256": hex.EncodeToString(sum[:]), "DataFile": "map.packed", "SeenUtc": seen}
	encoded, err := json.Marshal(meta)
	if err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(dir, "map.json")
	if err := os.WriteFile(path, encoded, 0600); err != nil {
		t.Fatal(err)
	}
	return path
}

func TestLiveTerrainBindingRequiresMatchingCaptureAndLiveEvidence(t *testing.T) {
	start := time.Now().UTC()
	path := testTerrainCapture(t, start.Add(time.Millisecond))
	e, err := New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 5, CatchUpDistance: 30,
			MaxAge: time.Second, TargetTrail: true}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer e.Close()
	binding := TerrainBinding{CaptureMetadataFile: path, ProcessID: 202,
		AreaID: "G2_4", Seed: 123, AreaHash: 77, OriginX: 0, OriginY: 0, CellSize: 1}
	bad := binding
	bad.AreaHash = 78
	if err := e.SetTerrainBinding(2, bad); err == nil {
		t.Fatal("capture hash mismatch accepted")
	}
	if err := e.SetTerrainBinding(2, binding); err != nil {
		t.Fatal(err)
	}
	preCapture := start.Add(500 * time.Microsecond)
	for _, slot := range []int{1, 2} {
		e.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: start, Origin: "live"})
	}
	observeGridSample(e, telemetry.Sample{ProcessID: 101, SeenUTC: preCapture, AreaHash: 77, X: 30, Y: 20})
	observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: preCapture, AreaHash: 77, X: 10, Y: 20})
	if got := e.Step(preCapture)[2]; got.State != follow.Waiting {
		t.Fatalf("sample older than capture moved: %+v", got)
	}
	for _, slot := range []int{1, 2} {
		e.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: start, Origin: "live"})
	}
	at := start.Add(2 * time.Millisecond)
	observeGridSample(e, telemetry.Sample{ProcessID: 101, SeenUTC: at, AreaHash: 77, X: 30, Y: 20})
	observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: at, AreaHash: 76, X: 10, Y: 20})
	if got := e.Step(at)[2]; got.State != follow.Waiting {
		t.Fatalf("wrong live hash moved: %+v", got)
	}
	observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: at.Add(time.Millisecond), AreaHash: 77, X: 10, Y: 20})
	if got := e.Step(at.Add(time.Millisecond))[2]; got.State == follow.Waiting {
		t.Fatalf("matching evidence did not reach terrain planner: %+v", got)
	}
	observeGridSample(e, telemetry.Sample{ProcessID: 101, SeenUTC: at.Add(1250 * time.Microsecond), AreaHash: 78, X: 30, Y: 20})
	if got := e.Step(at.Add(1250 * time.Microsecond))[2]; got.State != follow.Waiting {
		t.Fatalf("target hash mismatch moved: %+v", got)
	}
	observeGridSample(e, telemetry.Sample{ProcessID: 101, SeenUTC: at.Add(1400 * time.Microsecond), AreaHash: 77, X: 30, Y: 20})
	observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: at.Add(1500 * time.Microsecond), AreaHash: 77, X: 500, Y: 20})
	if got := e.Step(at.Add(1500 * time.Microsecond))[2]; got.State != follow.Waiting {
		t.Fatalf("position outside bound map moved: %+v", got)
	}
	e.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_4", Seed: 456,
		ObservedAt: at.Add(2 * time.Millisecond), Origin: "live"})
	observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: at.Add(3 * time.Millisecond), AreaHash: 77, X: 10, Y: 20})
	if got := e.Step(at.Add(3 * time.Millisecond))[2]; got.State != follow.Waiting {
		t.Fatalf("changed seed moved: %+v", got)
	}
}

func TestTerrainBindingFileResolvesCaptureBesideBinding(t *testing.T) {
	path := testTerrainCapture(t, time.Now().UTC())
	bindingFile := filepath.Join(filepath.Dir(path), "binding.json")
	if err := os.WriteFile(bindingFile, []byte(`{"capture_metadata_file":"map.json"}`), 0600); err != nil {
		t.Fatal(err)
	}
	binding, err := LoadTerrainBinding(bindingFile)
	if err != nil || binding.CaptureMetadataFile != path {
		t.Fatalf("relative capture path not resolved: %+v, %v", binding, err)
	}
}
