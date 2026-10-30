package combat

import (
	"errors"
	"fmt"
	"time"
)

// AutoCastRow preserves a stable identity for the target's per-row timer.
// The target stores this identity as the row object itself.
type AutoCastRow struct {
	ID         string `json:"id"`
	Input      string `json:"input"`
	Target     string `json:"target"`
	Trigger    string `json:"trigger"`
	CooldownMS int    `json:"cooldown_ms"`
	Enabled    bool   `json:"enabled"`
}

// AutoCastPulse is active for the target's 120 ms held interval.
type AutoCastPulse struct {
	Player int    `json:"player"`
	Input  string `json:"input"`
}

type autoCastTiming struct {
	next  time.Time
	start time.Time
	end   time.Time
}

type AutoCastEngine struct {
	rows  []AutoCastRow
	state map[string]autoCastTiming
}

func DefaultAutoCastRows() []AutoCastRow {
	return []AutoCastRow{
		{ID: "default-1", Input: "D-pad Up", Target: "Both", Trigger: "Timer", CooldownMS: 8250},
		{ID: "default-2", Input: "Y", Target: "P2", Trigger: "Timer", CooldownMS: 12000},
		{ID: "default-3", Input: "D-pad Up", Target: "Both", Trigger: "Timer", CooldownMS: 5000},
	}
}

func NewAutoCastEngine(rows []AutoCastRow) (*AutoCastEngine, error) {
	seen := make(map[string]bool, len(rows))
	for _, row := range rows {
		if row.ID == "" || seen[row.ID] ||
			!autoCastInputKnown(row.Input) || !autoCastTargetKnown(row.Target) ||
			(row.Trigger != "Timer" && row.Trigger != "After attack") ||
			row.CooldownMS < 250 || row.CooldownMS > 60000 {
			return nil, errors.New("invalid auto-cast timer row")
		}
		seen[row.ID] = true
	}
	return &AutoCastEngine{rows: append([]AutoCastRow(nil), rows...),
		state: make(map[string]autoCastTiming)}, nil
}

func autoCastInputKnown(input string) bool {
	switch input {
	case "D-pad Up", "D-pad Down", "A", "B", "X", "Y", "LB", "RB":
		return true
	default:
		return false
	}
}

func autoCastTargetKnown(target string) bool {
	switch target {
	case "Off", "P1", "P2", "Both", "P3", "P4", "Client 2 Pair",
		"P5", "P6", "Client 3 Pair", "All Four", "All Six":
		return true
	default:
		return false
	}
}

func autoCastIncludes(target string, player int) bool {
	switch target {
	case "P1", "P2", "P3", "P4", "P5", "P6":
		return target == fmt.Sprintf("P%d", player)
	case "Both":
		return player <= 2
	case "Client 2 Pair":
		return player >= 3 && player <= 4
	case "Client 3 Pair":
		return player >= 5 && player <= 6
	case "All Four":
		return player <= 4
	case "All Six":
		return player <= 6
	default:
		return false
	}
}

// Step mirrors the target's timer and attack-edge gates. jitter must be 1–100
// milliseconds, matching its Random.Shared.Next(1, 101) call.
func (engine *AutoCastEngine) Step(now time.Time, enabled, combatOnly, combatRecent,
	attackActive, attackEdge bool, players []int, dead map[int]bool, jitter func() int) ([]AutoCastPulse, error) {
	if now.IsZero() || jitter == nil {
		return nil, errors.New("auto-cast needs a clock and jitter source")
	}
	if !enabled || combatOnly && !combatRecent {
		return nil, nil
	}
	var pulses []AutoCastPulse
	for _, row := range engine.rows {
		if !row.Enabled {
			continue
		}
		state := engine.state[row.ID]
		if state.next.IsZero() {
			state.next = now.Add(time.Duration(row.CooldownMS) * time.Millisecond)
		}
		triggered := row.Trigger == "Timer" || attackActive && attackEdge
		if triggered && !now.Before(state.next) {
			delay := jitter()
			if delay < 1 || delay > 100 {
				return nil, errors.New("auto-cast jitter must be 1–100 ms")
			}
			state.start = now.Add(time.Duration(delay) * time.Millisecond)
			state.end = state.start.Add(120 * time.Millisecond)
			state.next = state.start.Add(time.Duration(row.CooldownMS) * time.Millisecond)
		}
		engine.state[row.ID] = state
		if now.Before(state.start) || !now.Before(state.end) {
			continue
		}
		for _, player := range players {
			if player >= 1 && player <= 6 && !dead[player] && autoCastIncludes(row.Target, player) {
				pulses = append(pulses, AutoCastPulse{Player: player, Input: row.Input})
			}
		}
	}
	return pulses, nil
}
