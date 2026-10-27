package live

import (
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"math"
	"strconv"
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

// OrderedClientFrame is the explicit input boundary for local-player vectors.
// A producer's reliability flag is a claim; the experimental static-vector
// producer still needs game validation before it can establish correct roles.
type OrderedClientFrame struct {
	ProcessID           int                  `json:"ProcessId"`
	SeenUTC             time.Time            `json:"SeenUtc"`
	ReceivedAt          time.Time            `json:"-"`
	TickMS              int64                `json:"TickMs"`
	AreaName            string               `json:"AreaName"`
	AreaHash            uint32               `json:"AreaHash"`
	AreaInstanceAddress string               `json:"AreaInstanceAddress"`
	VectorHeaderAddress string               `json:"VectorHeaderAddress"`
	OrderedAddresses    []string             `json:"OrderedAddresses"`
	CoordinateSpace     string               `json:"CoordinateSpace"`
	PlayerOrderReliable bool                 `json:"PlayerOrderReliable"`
	PlayerOrderSource   string               `json:"PlayerOrderSource"`
	Players             []OrderedLocalPlayer `json:"Players"`
}

type OrderedLocalPlayer struct {
	LocalPlayer     int      `json:"LocalPlayer"`
	X               float64  `json:"X"`
	Y               float64  `json:"Y"`
	Z               float64  `json:"Z"`
	Life            *float64 `json:"Life"`
	LifeMax         *float64 `json:"LifeMax"`
	Mana            *float64 `json:"Mana"`
	ManaMax         *float64 `json:"ManaMax"`
	EnergyShield    *float64 `json:"EnergyShield"`
	EnergyShieldMax *float64 `json:"EnergyShieldMax"`
}

const unverifiedExileCoreSlotSource = "unverified_exilecore_local_slot_candidate"
const experimentalStaticVectorSource = "experimental_static_vector_0.6.17"

func validStaticVectorPointer(value string) bool {
	if !strings.HasPrefix(value, "0x") || len(value) <= 2 {
		return false
	}
	address, err := strconv.ParseUint(value[2:], 16, 64)
	return err == nil && address >= 0x10000 && address < 0x7FFF_FFFF_FFFF
}

func validStaticVectorIdentity(frame OrderedClientFrame, roles []RoleBinding) bool {
	if frame.PlayerOrderSource != experimentalStaticVectorSource {
		return true
	}
	if frame.AreaHash == 0 || !validStaticVectorPointer(frame.AreaInstanceAddress) ||
		!validStaticVectorPointer(frame.VectorHeaderAddress) ||
		len(frame.OrderedAddresses) != len(roles) {
		return false
	}
	seen := make(map[uint64]bool, len(frame.OrderedAddresses))
	for _, value := range frame.OrderedAddresses {
		if !validStaticVectorPointer(value) {
			return false
		}
		address, _ := strconv.ParseUint(value[2:], 16, 64)
		if seen[address] {
			return false
		}
		seen[address] = true
	}
	return true
}

// DecodeOrderedClient is an explicit, opt-in transport boundary. The wire
// claim of reliable order is not proof that its producer read the game correctly.
func DecodeOrderedClient(data []byte, receivedAt time.Time) (OrderedClientFrame, error) {
	var wire struct {
		Kind string `json:"Kind"`
		OrderedClientFrame
	}
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&wire); err != nil {
		return OrderedClientFrame{}, err
	}
	var trailing any
	if err := decoder.Decode(&trailing); err != io.EOF {
		return OrderedClientFrame{}, errors.New("ordered frame has trailing data")
	}
	if wire.Kind != "ordered_client" || receivedAt.IsZero() || wire.ProcessID <= 0 ||
		wire.SeenUTC.IsZero() || wire.TickMS <= 0 || len(wire.Players) > 2 {
		return OrderedClientFrame{}, errors.New("invalid ordered client frame")
	}
	wire.ReceivedAt = receivedAt
	return wire.OrderedClientFrame, nil
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
		strings.EqualFold(strings.TrimSpace(frame.PlayerOrderSource), unverifiedExileCoreSlotSource) ||
		!validStaticVectorIdentity(frame, roles) ||
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
			(player.LifeMax != nil && *player.LifeMax <= 0) ||
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
		delete(e.vitals, role.Slot)
		delete(e.strictVitals, role.Slot)
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
