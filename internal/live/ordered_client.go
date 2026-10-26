package live

import (
	"math"
	"strings"
	"time"

	"partypilot/internal/telemetry"
)

// RoleBinding is an explicit party-role to client-local-player assignment.
// A process may own two roles, but their local slots must be 1 and 2.
type RoleBinding struct {
	Slot        int
	Client      int
	LocalPlayer int
	ProcessID   int
}

// OrderedClientFrame is a future input boundary for an independently verified
// local-player vector. The current ExileCore2 candidate scan does not produce
// this frame and must not be promoted into it without game validation.
type OrderedClientFrame struct {
	ProcessID           int
	SeenUTC             time.Time
	ReceivedAt          time.Time
	TickMS              int64
	AreaName            string
	AreaHash            uint32
	CoordinateSpace     string
	PlayerOrderReliable bool
	PlayerOrderSource   string
	Players             []OrderedLocalPlayer
}

type OrderedLocalPlayer struct {
	LocalPlayer                   int
	X, Y, Z                       float64
	Life, LifeMax                 *float64
	Mana, ManaMax                 *float64
	EnergyShield, EnergyShieldMax *float64
}

// ObserveOrderedClient commits a complete 1–2 player frame atomically. Missing,
// stale, duplicated, or unordered local slots cannot update just one role.
func (e *Engine) ObserveOrderedClient(frame OrderedClientFrame) bool {
	roles := e.pidToRoles[frame.ProcessID]
	if len(roles) == 0 {
		return false
	}
	if !frame.PlayerOrderReliable ||
		strings.TrimSpace(frame.PlayerOrderSource) == "" ||
		len(frame.Players) != len(roles) || frame.CoordinateSpace != "world" ||
		frame.SeenUTC.IsZero() || frame.ReceivedAt.IsZero() || frame.TickMS <= 0 {
		e.invalidateOrderedIfCurrent(roles, frame)
		return false
	}
	byLocal := make(map[int]OrderedLocalPlayer, len(frame.Players))
	for _, player := range frame.Players {
		if player.LocalPlayer < 1 || player.LocalPlayer > len(roles) ||
			!finitePosition(player.X) || !finitePosition(player.Y) ||
			!finitePosition(player.Z) || !validOrderedPool(player.Life, player.LifeMax) ||
			player.Life == nil || player.LifeMax == nil || *player.LifeMax <= 0 ||
			!validOrderedPool(player.Mana, player.ManaMax) ||
			!validOrderedPool(player.EnergyShield, player.EnergyShieldMax) {
			e.invalidateOrderedIfCurrent(roles, frame)
			return false
		}
		if _, exists := byLocal[player.LocalPlayer]; exists {
			e.invalidateOrderedIfCurrent(roles, frame)
			return false
		}
		byLocal[player.LocalPlayer] = player
	}
	updates := make(map[int]telemetry.Sample, len(roles))
	for _, role := range roles {
		player, exists := byLocal[role.LocalPlayer]
		if !exists {
			e.invalidateOrderedIfCurrent(roles, frame)
			return false
		}
		sample := telemetry.Sample{ProcessID: frame.ProcessID, SeenUTC: frame.SeenUTC,
			ReceivedAt: frame.ReceivedAt, TickMS: frame.TickMS,
			AreaName: frame.AreaName, AreaHash: frame.AreaHash,
			CoordinateSpace: frame.CoordinateSpace, X: player.X, Y: player.Y, Z: player.Z,
			Life: player.Life, LifeMax: player.LifeMax, Mana: player.Mana,
			ManaMax: player.ManaMax, EnergyShield: player.EnergyShield,
			EnergyShieldMax: player.EnergyShieldMax}
		if !validMovementSample(sample) || !e.sampleIsNew(role.Slot, sample) {
			return false
		}
		updates[role.Slot] = sample
	}
	for _, role := range roles {
		e.storeSample(role.Slot, updates[role.Slot])
	}
	return true
}

func (e *Engine) invalidateOrderedIfCurrent(roles []RoleBinding, frame OrderedClientFrame) {
	if frame.SeenUTC.IsZero() || frame.TickMS <= 0 {
		return
	}
	for _, role := range roles {
		if prior, exists := e.samples[role.Slot]; exists &&
			(frame.SeenUTC.Before(prior.SeenUTC) || frame.TickMS < prior.TickMS) {
			return // An older invalid packet cannot revoke a newer complete frame.
		}
	}
	for _, role := range roles {
		delete(e.samples, role.Slot)
		delete(e.terrainStable, role.Slot)
	}
	e.coordinator.ResetFollow()
}

func finitePosition(value float64) bool { return !math.IsNaN(value) && !math.IsInf(value, 0) }

func validOrderedPool(current, maximum *float64) bool {
	if current == nil && maximum == nil {
		return true
	}
	return current != nil && maximum != nil && finitePosition(*current) &&
		finitePosition(*maximum) && *current >= 0 && *maximum >= 0
}
