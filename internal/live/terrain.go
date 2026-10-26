package live

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	"partypilot/internal/navigation"
)

// TerrainBinding is an explicit operator assertion about one capture and one
// live instance. File validity alone cannot establish that assertion.
type TerrainBinding struct {
	CaptureMetadataFile string  `json:"capture_metadata_file"`
	ProcessID           int     `json:"process_id"`
	AreaID              string  `json:"area_id"`
	Seed                int64   `json:"seed"`
	AreaHash            uint32  `json:"area_hash"`
	OriginX             float64 `json:"origin_x"`
	OriginY             float64 `json:"origin_y"`
	CellSize            float64 `json:"cell_size"`
}

type boundTerrain struct {
	binding TerrainBinding
	grid    *navigation.Grid
	seen    time.Time
}

func LoadTerrainBinding(path string) (TerrainBinding, error) {
	file, err := os.Open(path)
	if err != nil {
		return TerrainBinding{}, err
	}
	defer file.Close()
	data, err := io.ReadAll(io.LimitReader(file, 64*1024+1))
	if err != nil || len(data) > 64*1024 {
		return TerrainBinding{}, errors.New("invalid terrain binding file")
	}
	var binding TerrainBinding
	if json.Unmarshal(data, &binding) != nil {
		return TerrainBinding{}, errors.New("invalid terrain binding JSON")
	}
	if binding.CaptureMetadataFile != "" && !filepath.IsAbs(binding.CaptureMetadataFile) {
		binding.CaptureMetadataFile = filepath.Join(filepath.Dir(path), binding.CaptureMetadataFile)
	}
	return binding, nil
}

func (e *Engine) SetTerrainBinding(slot int, binding TerrainBinding) error {
	if slot == e.leader || binding.ProcessID <= 0 || e.pidToSlot[binding.ProcessID] != slot ||
		strings.TrimSpace(binding.CaptureMetadataFile) == "" || strings.TrimSpace(binding.AreaID) == "" ||
		binding.Seed <= 0 || binding.AreaHash == 0 || math.IsNaN(binding.OriginX) ||
		math.IsInf(binding.OriginX, 0) || math.IsNaN(binding.OriginY) ||
		math.IsInf(binding.OriginY, 0) || math.IsNaN(binding.CellSize) ||
		math.IsInf(binding.CellSize, 0) || binding.CellSize <= 0 {
		return fmt.Errorf("slot %d: invalid terrain binding", slot)
	}
	capture, err := navigation.LoadCandidateCapture(binding.CaptureMetadataFile)
	if err != nil || capture.ProcessID != binding.ProcessID ||
		capture.AreaHash != strconv.FormatUint(uint64(binding.AreaHash), 10) || capture.SeenUTC.IsZero() {
		return fmt.Errorf("slot %d: capture identity or timestamp does not match binding", slot)
	}
	area := strings.ToLower(binding.AreaID) + fmt.Sprintf(":%d", binding.Seed)
	grid, err := navigation.GridFromPacked(area, binding.OriginX, binding.OriginY,
		binding.CellSize, capture.Data, capture.BytesPerRow, capture.Rows)
	if err != nil || !grid.ContainsPoint(capture.RawGridX, capture.RawGridY) {
		return fmt.Errorf("slot %d: capture coordinate mapping is outside terrain", slot)
	}
	if e.terrains == nil {
		e.terrains = make(map[int]boundTerrain)
	}
	e.terrains[slot] = boundTerrain{binding: binding, grid: grid, seen: capture.SeenUTC}
	e.coordinator.ResetFollow()
	return nil
}
