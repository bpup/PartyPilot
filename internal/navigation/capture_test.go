package navigation

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
)

func TestCandidateCaptureRequiresFileHashAndObservedPatch(t *testing.T) {
	directory := t.TempDir()
	data := bytes.Repeat([]byte{0x11}, 46*92)
	data[20*46+5] = 0x10
	dataPath := filepath.Join(directory, "terrain.packed")
	if err := os.WriteFile(dataPath, data, 0600); err != nil {
		t.Fatal(err)
	}
	digest := sha256.Sum256(data)
	meta := captureMetadata{Status: "candidate_capture_only", ProcessID: 123,
		AreaHash: "77", AreaAddress: "0x100000", Anchor: "ingame_data_address",
		PackedStart: "0x200000", RawGridX: 10, RawGridY: 20,
		Width: 92, Height: 92, BytesPerRow: 46, PackedBytes: len(data),
		PatchX: 10, PatchY: 20, PatchRows: []string{"01"},
		SHA256: hex.EncodeToString(digest[:]), DataFile: filepath.Base(dataPath)}
	metaPath := filepath.Join(directory, "terrain.json")
	write := func() {
		encoded, err := json.Marshal(meta)
		if err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(metaPath, encoded, 0600); err != nil {
			t.Fatal(err)
		}
	}
	write()
	capture, err := LoadCandidateCapture(metaPath)
	if err != nil || capture.BytesPerRow != 46 || capture.Rows != 92 ||
		!bytes.Equal(capture.Data, data) {
		t.Fatalf("valid capture not loaded: %+v, %v", capture, err)
	}
	meta.PatchRows = []string{"11"}
	write()
	if _, err := LoadCandidateCapture(metaPath); err == nil {
		t.Fatal("patch mismatch accepted")
	}
	meta.PatchRows = []string{"01"}
	meta.SHA256 = hex.EncodeToString(bytes.Repeat([]byte{0}, 32))
	write()
	if _, err := LoadCandidateCapture(metaPath); err == nil {
		t.Fatal("hash mismatch accepted")
	}
	meta.SHA256 = hex.EncodeToString(digest[:])
	meta.DataFile = "../terrain.packed"
	write()
	if _, err := LoadCandidateCapture(metaPath); err == nil {
		t.Fatal("capture path traversal accepted")
	}
}

func TestCandidateCaptureCorrelatedLayoutMatchesTargetBoundedRule(t *testing.T) {
	base := &CandidateCapture{Data: bytes.Repeat([]byte{0x11}, 50*100), BytesPerRow: 50, Rows: 100}
	peer := &CandidateCapture{Data: bytes.Clone(base.Data), BytesPerRow: 50, Rows: 100}
	peer.Data[0] = 0x20 // One blocked cell; another nonzero nibble remains walkable.
	peer.Data[1] = 0x10
	if correlated, different := base.CorrelatedLayout(peer); !correlated || different != 2 {
		t.Fatalf("two dynamic cells should correlate: %v, %d", correlated, different)
	}
	peer.Data[2] = 0x10
	if correlated, different := base.CorrelatedLayout(peer); correlated || different != 3 {
		t.Fatalf("third different cell should reject layout: %v, %d", correlated, different)
	}
	peer.Data[0], peer.Data[1], peer.Data[2] = 0x22, 0x11, 0x11
	if correlated, different := base.CorrelatedLayout(peer); !correlated || different != 0 {
		t.Fatalf("nonzero nibble magnitude changed walkability: %v, %d", correlated, different)
	}
	peer.Rows++
	if correlated, _ := base.CorrelatedLayout(peer); correlated {
		t.Fatal("different dimensions accepted")
	}
}
