package navigation

import (
	"crypto/sha256"
	"crypto/subtle"
	"encoding/hex"
	"encoding/json"
	"errors"
	"io"
	"os"
	"path/filepath"
	"time"
)

var ErrCandidateCapture = errors.New("invalid diagnostic terrain capture")

// CandidateCapture remains diagnostic. A valid file does not prove that its
// area identity or coordinate system matches a live game instance.
type CandidateCapture struct {
	Data        []byte
	BytesPerRow int
	Rows        int
	ProcessID   int
	AreaHash    string
	SeenUTC     time.Time
	RawGridX    float64
	RawGridY    float64
}

// CorrelatedLayout applies the supplied build's bounded walkability comparison
// to two immutable packed captures. Nibble magnitudes are normalized to
// blocked/nonblocked before comparison, as in NavigationTerrain.
func (c *CandidateCapture) CorrelatedLayout(other *CandidateCapture) (bool, int) {
	if c == nil || other == nil || c.BytesPerRow != other.BytesPerRow ||
		c.Rows != other.Rows || c.BytesPerRow <= 0 || c.Rows <= 0 ||
		len(c.Data) != c.BytesPerRow*c.Rows || len(other.Data) != len(c.Data) {
		return false, 0
	}
	cellCount := len(c.Data) * 2
	limit := min(4096, cellCount/5000)
	different := 0
	for index, value := range c.Data {
		peer := other.Data[index]
		if (value&15 == 0) != (peer&15 == 0) {
			different++
		}
		if (value>>4 == 0) != (peer>>4 == 0) {
			different++
		}
		if different > limit {
			return false, different
		}
	}
	return true, different
}

type captureMetadata struct {
	Status      string    `json:"Status"`
	ProcessID   int       `json:"ProcessId"`
	AreaHash    string    `json:"AreaHash"`
	AreaAddress string    `json:"AreaAddress"`
	Anchor      string    `json:"Anchor"`
	PackedStart string    `json:"PackedStart"`
	RawGridX    float64   `json:"RawGridX"`
	RawGridY    float64   `json:"RawGridY"`
	Width       int       `json:"Width"`
	Height      int       `json:"Height"`
	BytesPerRow int       `json:"BytesPerRow"`
	PackedBytes int       `json:"PackedBytes"`
	PatchX      int       `json:"PatchX"`
	PatchY      int       `json:"PatchY"`
	PatchRows   []string  `json:"PatchRows"`
	SHA256      string    `json:"Sha256"`
	DataFile    string    `json:"DataFile"`
	SeenUTC     time.Time `json:"SeenUtc"`
}

func LoadCandidateCapture(metadataPath string) (*CandidateCapture, error) {
	metadataFile, err := os.Open(metadataPath)
	if err != nil {
		return nil, ErrCandidateCapture
	}
	metadataBytes, err := io.ReadAll(io.LimitReader(metadataFile, 64*1024+1))
	closeErr := metadataFile.Close()
	if err != nil || closeErr != nil || len(metadataBytes) > 64*1024 {
		return nil, ErrCandidateCapture
	}
	var meta captureMetadata
	if json.Unmarshal(metadataBytes, &meta) != nil || meta.Status != "candidate_capture_only" ||
		meta.ProcessID <= 0 || meta.AreaHash == "" || meta.AreaAddress == "" ||
		(meta.Anchor != "state_pointer_static_656" && meta.Anchor != "ingame_data_address") ||
		meta.PackedStart == "" || meta.BytesPerRow < 1 || meta.BytesPerRow > 50000 ||
		meta.Height < 1 || meta.Height > 100000 || meta.Width != meta.BytesPerRow*2 ||
		meta.PackedBytes != meta.BytesPerRow*meta.Height ||
		meta.PackedBytes < 4096 || meta.PackedBytes > 64*1024*1024 ||
		meta.RawGridX < 0 || meta.RawGridY < 0 ||
		!finite(meta.RawGridX) || !finite(meta.RawGridY) ||
		meta.RawGridX >= float64(meta.Width) || meta.RawGridY >= float64(meta.Height) ||
		meta.DataFile == "" || meta.DataFile != filepath.Base(meta.DataFile) ||
		filepath.Ext(meta.DataFile) != ".packed" || len(meta.SHA256) != 64 {
		return nil, ErrCandidateCapture
	}
	path := filepath.Join(filepath.Dir(metadataPath), meta.DataFile)
	file, err := os.Open(path)
	if err != nil {
		return nil, ErrCandidateCapture
	}
	defer file.Close()
	info, err := file.Stat()
	if err != nil || !info.Mode().IsRegular() || info.Size() != int64(meta.PackedBytes) {
		return nil, ErrCandidateCapture
	}
	data, err := io.ReadAll(io.LimitReader(file, 64*1024*1024+1))
	if err != nil || len(data) != meta.PackedBytes {
		return nil, ErrCandidateCapture
	}
	want, err := hex.DecodeString(meta.SHA256)
	actual := sha256.Sum256(data)
	if err != nil || subtle.ConstantTimeCompare(want, actual[:]) != 1 ||
		!capturePatchMatches(data, meta) {
		return nil, ErrCandidateCapture
	}
	return &CandidateCapture{Data: data, BytesPerRow: meta.BytesPerRow,
		Rows: meta.Height, ProcessID: meta.ProcessID, AreaHash: meta.AreaHash,
		SeenUTC: meta.SeenUTC, RawGridX: meta.RawGridX, RawGridY: meta.RawGridY}, nil
}

func capturePatchMatches(data []byte, meta captureMetadata) bool {
	if meta.PatchX < 0 || meta.PatchY < 0 || len(meta.PatchRows) < 1 ||
		len(meta.PatchRows) > 9 || meta.PatchY+len(meta.PatchRows) > meta.Height {
		return false
	}
	for y, row := range meta.PatchRows {
		if len(row) < 1 || len(row) > 9 || meta.PatchX+len(row) > meta.Width {
			return false
		}
		for x := range row {
			cx := meta.PatchX + x
			value := data[(meta.PatchY+y)*meta.BytesPerRow+cx/2]
			if cx&1 == 0 {
				value &= 15
			} else {
				value >>= 4
			}
			want := byte('1')
			if value == 0 {
				want = '0'
			}
			if row[x] != want {
				return false
			}
		}
	}
	return true
}
