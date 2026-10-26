package telemetry

import (
	"encoding/json"
	"errors"
	"math"
	"time"
)

type Sample struct {
	ProcessID int       `json:"ProcessId"`
	SeenUTC   time.Time `json:"SeenUtc"`
	AreaName  string    `json:"AreaName"`
	AreaHash  uint32    `json:"AreaHash"`
	X         float64   `json:"X"`
	Y         float64   `json:"Y"`
	Z         float64   `json:"Z"`
}

var ErrInvalidSample = errors.New("invalid telemetry sample")

func Decode(data []byte) (Sample, error) {
	var item Sample
	if err := json.Unmarshal(data, &item); err != nil {
		return Sample{}, err
	}
	if item.ProcessID <= 0 || item.SeenUTC.IsZero() ||
		!finite(item.X) || !finite(item.Y) || !finite(item.Z) {
		return Sample{}, ErrInvalidSample
	}
	return item, nil
}

func finite(value float64) bool {
	return !math.IsNaN(value) && !math.IsInf(value, 0)
}
