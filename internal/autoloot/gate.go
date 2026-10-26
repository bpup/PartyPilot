package autoloot

import (
	"errors"
	"strings"
	"time"
)

// These durations and the one-frame admission rule are observed in the
// supplied 0.6.17 AutoLootGate and ShellViewModel static code.
const (
	CardMaxAge = 350 * time.Millisecond
	PulseTime  = 80 * time.Millisecond
	RetryAfter = 180 * time.Millisecond
)

// Card represents a controller-selected item card. World loot and UI hover
// observations must never be converted to this type without a verified source.
type Card struct {
	SelectedItem string    `json:"selected_item"`
	BaseID       string    `json:"base_id"`
	Player       int       `json:"player"`
	Targetable   bool      `json:"targetable"`
	ReceivedAt   time.Time `json:"received_at"`
}

// Conditions are separate evidence supplied by the caller. The replay tool
// has no live producer for these fields; their zero values all close the gate.
type Conditions struct {
	CatalogApproved         bool `json:"catalog_approved"`
	CapacityAllows          bool `json:"capacity_allows"`
	InventoryLimitAllows    bool `json:"inventory_limit_allows"`
	MenuClear               bool `json:"menu_clear"`
	OwnInventoryClosed      bool `json:"own_inventory_closed"`
	InventoryAutomationFree bool `json:"inventory_automation_free"`
	Alive                   bool `json:"alive"`
}

type Frame struct {
	Now           time.Time  `json:"now"`
	Player        int        `json:"player"`
	FrameSequence int64      `json:"frame_sequence"`
	Card          *Card      `json:"card"`
	Conditions    Conditions `json:"conditions"`
}

type Decision struct {
	Player      int    `json:"player"`
	PulseActive bool   `json:"pulse_active"`
	Triggered   bool   `json:"triggered"`
	Attempt     int    `json:"attempt"`
	Reason      string `json:"reason"`
}

type playerState struct {
	lastTime  time.Time
	lastFrame int64
	seenFrame bool
	lastSafe  bool
	item      string
	latched   string
	attempts  int
	pulseEnd  time.Time
	nextTry   time.Time
}

type Gate struct{ players [6]playerState }

func NewGate() *Gate { return &Gate{} }

func (gate *Gate) Reset(player int) error {
	if player < 1 || player > 6 {
		return errors.New("player must be 1–6")
	}
	gate.players[player-1] = playerState{}
	return nil
}

// Step evaluates explicit, authoritative card and safety evidence. It never
// sends controller input. A caller must separately serialize valid pulses.
func (gate *Gate) Step(frame Frame) (Decision, error) {
	if frame.Player < 1 || frame.Player > 6 || frame.Now.IsZero() || frame.FrameSequence < 0 {
		return Decision{}, errors.New("invalid player, time, or frame sequence")
	}
	state := &gate.players[frame.Player-1]
	if (!state.lastTime.IsZero() && frame.Now.Before(state.lastTime)) ||
		(state.seenFrame && frame.FrameSequence < state.lastFrame) {
		return Decision{}, errors.New("out-of-order item card frame")
	}
	state.lastTime = frame.Now
	newFrame := !state.seenFrame || frame.FrameSequence != state.lastFrame
	state.lastFrame, state.seenFrame = frame.FrameSequence, true
	result := Decision{Player: frame.Player}
	card := frame.Card
	if card == nil {
		if newFrame {
			state.item, state.latched, state.attempts = "", "", 0
		}
		state.lastSafe = false
		state.pulseEnd = time.Time{}
		result.Reason = "no selected item card"
		return result, nil
	}
	localPlayer := (frame.Player-1)%2 + 1
	if card.Player != localPlayer || !card.Targetable || strings.TrimSpace(card.SelectedItem) == "" ||
		strings.TrimSpace(card.BaseID) == "" || card.ReceivedAt.IsZero() || card.ReceivedAt.After(frame.Now) ||
		frame.Now.Sub(card.ReceivedAt) > CardMaxAge {
		if newFrame {
			state.item, state.latched, state.attempts = "", "", 0
		}
		state.pulseEnd = time.Time{}
		state.lastSafe = false
		result.Reason = "invalid, mismatched, or stale item card"
		return result, nil
	}
	ready := frame.Conditions
	if !ready.CatalogApproved || !ready.CapacityAllows || !ready.InventoryLimitAllows ||
		!ready.MenuClear || !ready.OwnInventoryClosed || !ready.InventoryAutomationFree || !ready.Alive {
		state.pulseEnd = time.Time{}
		state.lastSafe = false
		state.item = ""
		result.Reason = "pickup safety evidence incomplete"
		return result, nil
	}
	safeChanged := !state.lastSafe
	state.lastSafe = true
	if newFrame || safeChanged {
		if !strings.EqualFold(state.item, card.SelectedItem) {
			state.item = card.SelectedItem
		}
		newItem := !strings.EqualFold(state.latched, card.SelectedItem)
		if newItem || !frame.Now.Before(state.nextTry) {
			state.latched = card.SelectedItem
			if newItem {
				state.attempts = 1
			} else if state.attempts < int(^uint(0)>>1) {
				state.attempts++
			}
			state.pulseEnd = frame.Now.Add(PulseTime)
			state.nextTry = frame.Now.Add(RetryAfter)
			result.Triggered = true
		}
	}
	result.PulseActive = frame.Now.Before(state.pulseEnd)
	result.Attempt = state.attempts
	if result.PulseActive {
		result.Reason = "pickup pulse requested"
	} else {
		result.Reason = "waiting for a new card frame or retry interval"
	}
	return result, nil
}
