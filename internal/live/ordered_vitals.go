package live

import (
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"strings"
	"time"

	"partypilot/internal/telemetry"
)

// OrderedVitalsFrame is a slower, process-bound resource observation. It never
// changes movement positions and requires the same complete local slot order.
type OrderedVitalsFrame struct {
	ProcessID           int                  `json:"ProcessId"`
	SeenUTC             time.Time            `json:"SeenUtc"`
	ReceivedAt          time.Time            `json:"-"`
	TickMS              int64                `json:"TickMs"`
	AreaHash            uint32               `json:"AreaHash"`
	PlayerOrderReliable bool                 `json:"PlayerOrderReliable"`
	PlayerOrderSource   string               `json:"PlayerOrderSource"`
	Players             []OrderedLocalVitals `json:"Players"`
}

type OrderedLocalVitals struct {
	LocalPlayer     int      `json:"LocalPlayer"`
	Life            *float64 `json:"Life"`
	LifeMax         *float64 `json:"LifeMax"`
	Mana            *float64 `json:"Mana"`
	ManaMax         *float64 `json:"ManaMax"`
	EnergyShield    *float64 `json:"EnergyShield"`
	EnergyShieldMax *float64 `json:"EnergyShieldMax"`
}

// DecodeOrderedVitals keeps the slow feed separate from the fast motion wire.
// The source's reliability flag remains a claim until its producer is tested.
func DecodeOrderedVitals(data []byte, receivedAt time.Time) (OrderedVitalsFrame, error) {
	var wire struct {
		Kind string `json:"Kind"`
		OrderedVitalsFrame
	}
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&wire); err != nil {
		return OrderedVitalsFrame{}, err
	}
	var trailing any
	if err := decoder.Decode(&trailing); err != io.EOF {
		return OrderedVitalsFrame{}, errors.New("ordered vitals have trailing data")
	}
	if wire.Kind != "ordered_vitals" || wire.ProcessID <= 0 ||
		wire.SeenUTC.IsZero() || wire.TickMS <= 0 || receivedAt.IsZero() ||
		len(wire.Players) > 2 {
		return OrderedVitalsFrame{}, errors.New("invalid ordered vitals frame")
	}
	wire.ReceivedAt = receivedAt
	return wire.OrderedVitalsFrame, nil
}

// ObserveOrderedVitals commits all local roles together. An incomplete current
// slow frame withdraws old resource values while leaving fast positions alone.
func (e *Engine) ObserveOrderedVitals(frame OrderedVitalsFrame) bool {
	roles := e.pidToRoles[frame.ProcessID]
	if len(roles) == 0 {
		return false
	}
	if !frame.PlayerOrderReliable || strings.TrimSpace(frame.PlayerOrderSource) == "" ||
		len(frame.Players) != len(roles) || frame.SeenUTC.IsZero() ||
		frame.ReceivedAt.IsZero() || frame.TickMS <= 0 {
		e.invalidateVitalsIfCurrent(roles, frame)
		return false
	}
	byLocal := make(map[int]OrderedLocalVitals, len(frame.Players))
	for _, player := range frame.Players {
		if player.LocalPlayer < 1 || player.LocalPlayer > len(roles) ||
			player.Life == nil || player.LifeMax == nil ||
			!validOrderedPool(player.Life, player.LifeMax) || *player.LifeMax <= 0 ||
			!validOrderedPool(player.Mana, player.ManaMax) ||
			!validOrderedPool(player.EnergyShield, player.EnergyShieldMax) {
			e.invalidateVitalsIfCurrent(roles, frame)
			return false
		}
		if _, exists := byLocal[player.LocalPlayer]; exists {
			e.invalidateVitalsIfCurrent(roles, frame)
			return false
		}
		byLocal[player.LocalPlayer] = player
	}
	updates := make(map[int]telemetry.Sample, len(roles))
	for _, role := range roles {
		player, exists := byLocal[role.LocalPlayer]
		if !exists {
			e.invalidateVitalsIfCurrent(roles, frame)
			return false
		}
		if prior, exists := e.vitals[role.Slot]; exists &&
			(frame.SeenUTC.Before(prior.SeenUTC) || frame.TickMS < prior.TickMS ||
				frame.ReceivedAt.Before(prior.ReceivedAt)) {
			return false
		}
		updates[role.Slot] = telemetry.Sample{ProcessID: frame.ProcessID,
			SeenUTC: frame.SeenUTC, ReceivedAt: frame.ReceivedAt, TickMS: frame.TickMS,
			AreaHash: frame.AreaHash, Life: player.Life, LifeMax: player.LifeMax,
			Mana: player.Mana, ManaMax: player.ManaMax,
			EnergyShield: player.EnergyShield, EnergyShieldMax: player.EnergyShieldMax}
	}
	for _, role := range roles {
		e.vitals[role.Slot] = updates[role.Slot]
		e.strictVitals[role.Slot] = true
	}
	return true
}

func (e *Engine) invalidateVitalsIfCurrent(roles []RoleBinding, frame OrderedVitalsFrame) {
	if frame.SeenUTC.IsZero() || frame.TickMS <= 0 {
		return
	}
	for _, role := range roles {
		if prior, exists := e.vitals[role.Slot]; exists &&
			(frame.SeenUTC.Before(prior.SeenUTC) || frame.TickMS < prior.TickMS) {
			return
		}
	}
	for _, role := range roles {
		delete(e.vitals, role.Slot)
		delete(e.strictVitals, role.Slot)
	}
}
