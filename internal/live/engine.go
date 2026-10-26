package live

import (
	"fmt"
	"sort"
	"strings"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/focus"
	"partypilot/internal/follow"
	"partypilot/internal/party"
	"partypilot/internal/partylog"
	"partypilot/internal/telemetry"
)

// Engine joins one read-only sensor and one independent log per game client.
// Its output is a movement decision; it does not establish game input mapping.
type Engine struct {
	coordinator *party.Coordinator
	leader      int
	logAge      time.Duration
	pidToSlot   map[int]int
	slots       []int
	samples     map[int]telemetry.Sample
	areas       map[int]arealog.Signal
	leaderName  string
	stopped     string
}

func (e *Engine) SetLeaderName(name string) { e.leaderName = strings.TrimSpace(name) }

// ObserveParty latches a stop. A later position or area update cannot resume
// following; restarting the session requires an explicit operator action.
func (e *Engine) ObserveParty(event partylog.Event) bool {
	configured := false
	for _, slot := range e.slots {
		configured = configured || slot == event.ClientSlot
	}
	if !configured {
		return false
	}
	reason := ""
	switch event.Kind {
	case partylog.Disbanded:
		reason = "party disbanded"
	case partylog.LocalLeft:
		reason = "local client left party"
	case partylog.MemberLeft:
		if e.leaderName != "" && strings.EqualFold(strings.TrimSpace(event.MemberName), e.leaderName) {
			reason = "followed member left party"
		}
	}
	if reason == "" {
		return false
	}
	e.stopped = reason
	e.coordinator.ResetFollow()
	return true
}

func New(roster map[int]int, leader int, config follow.Config, logAge time.Duration) (*Engine, error) {
	if len(roster) < 2 || len(roster) > 3 || logAge <= 0 {
		return nil, fmt.Errorf("live sensor supports 2–3 clients with positive log age")
	}
	slots := make([]int, 0, len(roster))
	for slot := range roster {
		slots = append(slots, slot)
	}
	sort.Ints(slots)
	players := make([]focus.Player, 0, len(slots))
	pidToSlot := make(map[int]int, len(slots))
	for index, slot := range slots {
		pid := roster[slot]
		if slot < 1 || slot > 6 || pid <= 0 {
			return nil, fmt.Errorf("invalid slot or process ID")
		}
		if _, exists := pidToSlot[pid]; exists {
			return nil, fmt.Errorf("duplicate process ID")
		}
		pidToSlot[pid] = slot
		players = append(players, focus.Player{Slot: slot, Client: index + 1})
	}
	coordinator, err := party.New(players, leader, config)
	if err != nil {
		return nil, err
	}
	return &Engine{coordinator: coordinator, leader: leader, logAge: logAge,
		pidToSlot: pidToSlot, slots: slots, samples: make(map[int]telemetry.Sample), areas: make(map[int]arealog.Signal)}, nil
}

func (e *Engine) ObserveSample(sample telemetry.Sample) bool {
	slot, exists := e.pidToSlot[sample.ProcessID]
	if !exists || sample.SeenUTC.IsZero() {
		return false
	}
	if previous, exists := e.samples[slot]; exists && sample.SeenUTC.Before(previous.SeenUTC) {
		return false
	}
	e.samples[slot] = sample
	return true
}

func (e *Engine) ObserveArea(signal arealog.Signal) bool {
	configured := false
	for _, slot := range e.slots {
		configured = configured || slot == signal.Slot
	}
	if !configured || signal.AreaID == "" || signal.Seed <= 0 || signal.ObservedAt.IsZero() {
		return false
	}
	if previous, exists := e.areas[signal.Slot]; exists && signal.ObservedAt.Before(previous.ObservedAt) {
		return false
	}
	if previous, exists := e.areas[signal.Slot]; exists &&
		(!strings.EqualFold(previous.AreaID, signal.AreaID) || previous.Seed != signal.Seed) {
		e.coordinator.ResetFollow()
	}
	e.areas[signal.Slot] = signal
	return true
}

func (e *Engine) Step(now time.Time) map[int]follow.Command {
	if e.stopped != "" {
		commands := make(map[int]follow.Command, len(e.slots)-1)
		for _, slot := range e.slots {
			if slot != e.leader {
				commands[slot] = follow.Command{State: follow.Waiting, Reason: e.stopped}
			}
		}
		return commands
	}
	positions := make(map[int]follow.Position, len(e.slots))
	leaderArea, hasLeaderArea := e.areas[e.leader]
	for _, slot := range e.slots {
		sample, hasSample := e.samples[slot]
		area, hasArea := e.areas[slot]
		if !hasSample || !hasArea || sample.SeenUTC.Before(area.ObservedAt) {
			continue
		}
		if slot != e.leader {
			if !hasLeaderArea || arealog.Compare(now, e.logAge, e.leader, slot, &leaderArea, &area).Status != "candidate_same_instance" {
				continue
			}
		} else if area.Origin != "live" || area.ObservedAt.After(now) || now.Sub(area.ObservedAt) > e.logAge {
			continue
		}
		positions[slot] = follow.Position{
			X: sample.X, Y: sample.Y, Seen: sample.SeenUTC, Valid: true,
			Area: strings.ToLower(leaderArea.AreaID) + fmt.Sprintf(":%d", leaderArea.Seed),
		}
	}
	return e.coordinator.Step(now, positions)
}
