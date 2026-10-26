package telemetry

import (
	"encoding/json"
	"errors"
	"math"
	"time"
)

type Sample struct {
	ProcessID       int       `json:"ProcessId"`
	SeenUTC         time.Time `json:"SeenUtc"`
	AreaName        string    `json:"AreaName"`
	AreaHash        uint32    `json:"AreaHash"`
	X               float64   `json:"X"`
	Y               float64   `json:"Y"`
	Z               float64   `json:"Z"`
	Life            *float64  `json:"Life,omitempty"`
	LifeMax         *float64  `json:"LifeMax,omitempty"`
	Mana            *float64  `json:"Mana,omitempty"`
	ManaMax         *float64  `json:"ManaMax,omitempty"`
	EnergyShield    *float64  `json:"EnergyShield,omitempty"`
	EnergyShieldMax *float64  `json:"EnergyShieldMax,omitempty"`
}

var ErrInvalidSample = errors.New("invalid telemetry sample")

func Decode(data []byte) (Sample, error) {
	var item Sample
	if err := json.Unmarshal(data, &item); err != nil {
		return Sample{}, err
	}
	if item.ProcessID <= 0 || item.SeenUTC.IsZero() ||
		!finite(item.X) || !finite(item.Y) || !finite(item.Z) ||
		!validPool(item.Life, item.LifeMax) || !validPool(item.Mana, item.ManaMax) ||
		!validPool(item.EnergyShield, item.EnergyShieldMax) {
		return Sample{}, ErrInvalidSample
	}
	return item, nil
}

func validPool(current, maximum *float64) bool {
	if current == nil && maximum == nil {
		return true
	}
	return current != nil && maximum != nil && finite(*current) && finite(*maximum) &&
		*current >= 0 && *maximum >= 0
}

func finite(value float64) bool {
	return !math.IsNaN(value) && !math.IsInf(value, 0)
}
