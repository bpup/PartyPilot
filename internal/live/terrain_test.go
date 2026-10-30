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

func testTerrainCapture(t *testing.T, seen time.Time, pid int, changedCells ...int) string {
	t.Helper()
	dir := t.TempDir()
	data := bytes.Repeat([]byte{0x11}, 64*64)
	for _, cell := range changedCells {
		index := cell / 2
		if cell&1 == 0 {
			data[index] &= 0xf0
		} else {
			data[index] &= 0x0f
		}
	}
	if err := os.WriteFile(filepath.Join(dir, "map.packed"), data, 0600); err != nil {
		t.Fatal(err)
	}
	sum := sha256.Sum256(data)
	meta := map[string]any{"Status": "candidate_capture_only", "ProcessId": pid,
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
	path := testTerrainCapture(t, start.Add(time.Millisecond), 202)
	leaderPath := testTerrainCapture(t, start.Add(time.Millisecond), 101)
	badLeaderPath := testTerrainCapture(t, start.Add(time.Millisecond), 101, 0, 1)
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
	if got := e.Step(start)[2]; got.State != follow.Waiting {
		t.Fatalf("missing target terrain moved: %+v", got)
	}
	leaderBinding := binding
	leaderBinding.ProcessID = 101
	leaderBinding.CaptureMetadataFile = badLeaderPath
	if err := e.SetTerrainBinding(1, leaderBinding); err == nil {
		t.Fatal("uncorrelated leader layout accepted")
	}
	leaderBinding.CaptureMetadataFile = leaderPath
	if err := e.SetTerrainBinding(1, leaderBinding); err != nil {
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
	if got := e.Step(at.Add(time.Millisecond))[2]; got.State != follow.Waiting ||
		got.Reason != "terrain still settling" {
		t.Fatalf("one terrain/motion frame moved: %+v", got)
	}
	if got := e.Step(at.Add(time.Millisecond))[2]; got.State != follow.Waiting {
		t.Fatalf("repeated frame counted twice: %+v", got)
	}
	for frame := 2; frame <= 3; frame++ {
		frameAt := at.Add(time.Duration(frame) * time.Millisecond)
		observeGridSample(e, telemetry.Sample{ProcessID: 101, SeenUTC: frameAt, AreaHash: 77, X: 30, Y: 20})
		observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: frameAt, AreaHash: 77, X: 10, Y: 20})
		got := e.Step(frameAt)[2]
		if frame == 2 && got.State != follow.Waiting || frame == 3 && got.State == follow.Waiting {
			t.Fatalf("frame %d did not follow three-frame gate: %+v", frame, got)
		}
	}
	observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: at.Add(3200 * time.Microsecond), AreaHash: 78, X: 10, Y: 20})
	observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: at.Add(3400 * time.Microsecond), AreaHash: 77, X: 10, Y: 20})
	if got := e.Step(at.Add(3400 * time.Microsecond))[2]; got.State != follow.Waiting {
		t.Fatalf("unobserved bad sample failed to reset stability: %+v", got)
	}
	observeGridSample(e, telemetry.Sample{ProcessID: 101, SeenUTC: at.Add(4250 * time.Microsecond), AreaHash: 78, X: 30, Y: 20})
	if got := e.Step(at.Add(4250 * time.Microsecond))[2]; got.State != follow.Waiting {
		t.Fatalf("target hash mismatch moved: %+v", got)
	}
	observeGridSample(e, telemetry.Sample{ProcessID: 101, SeenUTC: at.Add(4400 * time.Microsecond), AreaHash: 77, X: 30, Y: 20})
	observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: at.Add(4500 * time.Microsecond), AreaHash: 77, X: 500, Y: 20})
	if got := e.Step(at.Add(4500 * time.Microsecond))[2]; got.State != follow.Waiting {
		t.Fatalf("position outside bound map moved: %+v", got)
	}
	e.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_4", Seed: 456,
		ObservedAt: at.Add(5 * time.Millisecond), Origin: "live"})
	observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: at.Add(6 * time.Millisecond), AreaHash: 77, X: 10, Y: 20})
	if got := e.Step(at.Add(6 * time.Millisecond))[2]; got.State != follow.Waiting {
		t.Fatalf("changed seed moved: %+v", got)
	}
	for _, slot := range []int{1, 2} {
		e.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: at.Add(7 * time.Millisecond), Origin: "live"})
	}
	for frame := 1; frame <= 3; frame++ {
		frameAt := at.Add(time.Duration(7+frame) * time.Millisecond)
		observeGridSample(e, telemetry.Sample{ProcessID: 101, SeenUTC: frameAt, AreaHash: 77, X: 30, Y: 20})
		observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: frameAt, AreaHash: 77, X: 10, Y: 20})
		got := e.Step(frameAt)[2]
		if frame < 3 && got.State != follow.Waiting || frame == 3 && got.State == follow.Waiting {
			t.Fatalf("area return frame %d did not restart stability gate: %+v", frame, got)
		}
	}
	for _, slot := range []int{1, 2} {
		e.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: at.Add(11 * time.Millisecond), Origin: "live"})
	}
	frameAt := at.Add(12 * time.Millisecond)
	observeGridSample(e, telemetry.Sample{ProcessID: 101, SeenUTC: frameAt, AreaHash: 77, X: 30, Y: 20})
	observeGridSample(e, telemetry.Sample{ProcessID: 202, SeenUTC: frameAt, AreaHash: 77, X: 10, Y: 20})
	if got := e.Step(frameAt)[2]; got.State != follow.Waiting {
		t.Fatalf("new generation signal with same area bypassed stability gate: %+v", got)
	}
}

func TestTerrainBindingFileResolvesCaptureBesideBinding(t *testing.T) {
	path := testTerrainCapture(t, time.Now().UTC(), 202)
	bindingFile := filepath.Join(filepath.Dir(path), "binding.json")
	if err := os.WriteFile(bindingFile, []byte(`{"capture_metadata_file":"map.json"}`), 0600); err != nil {
		t.Fatal(err)
	}
	binding, err := LoadTerrainBinding(bindingFile)
	if err != nil || binding.CaptureMetadataFile != path {
		t.Fatalf("relative capture path not resolved: %+v, %v", binding, err)
	}
}

func TestTwoPlayerClientSharesOneValidatedTerrain(t *testing.T) {
	path := testTerrainCapture(t, time.Now().UTC(), 202)
	e, err := NewWithBindings([]RoleBinding{
		{Slot: 1, Client: 1, LocalPlayer: 1, ProcessID: 101},
		{Slot: 2, Client: 2, LocalPlayer: 1, ProcessID: 202},
		{Slot: 3, Client: 2, LocalPlayer: 2, ProcessID: 202},
	}, 1, follow.Config{StopDistance: 2, ResumeDistance: 5,
		CatchUpDistance: 30, MaxAge: time.Second, TargetTrail: true}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer e.Close()
	binding := TerrainBinding{CaptureMetadataFile: path, ProcessID: 202,
		AreaID: "G2_4", Seed: 123, AreaHash: 77, CellSize: 1}
	if err := e.SetTerrainBinding(2, binding); err != nil {
		t.Fatal(err)
	}
	if err := e.SetTerrainBinding(3, binding); err != nil {
		t.Fatal(err)
	}
	if e.terrains[2].grid != e.terrains[3].grid ||
		e.terrains[2].capture != e.terrains[3].capture {
		t.Fatal("same client did not share validated terrain")
	}
	changed := binding
	changed.Seed++
	if err := e.SetTerrainBinding(3, changed); err == nil {
		t.Fatal("same client accepted conflicting terrain identity")
	}
}
