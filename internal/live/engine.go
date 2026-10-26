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
	coordinator      *party.Coordinator
	leader           int
	baseLeader       int
	lastFocusedLeads bool
	logAge           time.Duration
	sampleAge        time.Duration
	pidToSlot        map[int]int
	slots            []int
	samples          map[int]telemetry.Sample
	areas            map[int]arealog.Signal
	leaderName       string
	memberNames      map[int]string
	stopped          string
}

func (e *Engine) SetLeaderName(name string) {
	e.memberNames[e.baseLeader] = strings.TrimSpace(name)
	e.leaderName = e.memberNames[e.leader]
}

func (e *Engine) SetMemberName(slot int, name string) error {
	known := false
	for _, candidate := range e.slots {
		known = known || candidate == slot
	}
	name = strings.TrimSpace(name)
	if !known || name == "" || len(name) > 128 {
		return fmt.Errorf("invalid member name for slot %d", slot)
	}
	e.memberNames[slot] = name
	if e.leader == slot {
		e.leaderName = name
	}
	return nil
}

func (e *Engine) SetLastFocusedLeads(enabled bool) {
	e.lastFocusedLeads = enabled
	e.coordinator.SetLastFocusedLeads(enabled)
}

func (e *Engine) Leader() int { return e.leader }

func (e *Engine) ObserveManualFocus(slot int) error {
	if !e.lastFocusedLeads {
		return fmt.Errorf("last-focused leadership is disabled")
	}
	if err := e.coordinator.Focus(slot); err != nil {
		return err
	}
	e.leader = slot
	e.leaderName = e.memberNames[slot]
	return nil
}

func (e *Engine) Stop(reason string) {
	if e.stopped == "" {
		e.stopped = reason
	}
	e.coordinator.ResetFollow()
}

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
	e.Stop(reason)
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
	return &Engine{coordinator: coordinator, leader: leader, baseLeader: leader,
		logAge: logAge, sampleAge: config.MaxAge, memberNames: make(map[int]string),
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
	deadFollowers := make(map[int]bool)
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
		if slot != e.leader && sample.Life != nil && *sample.Life <= 0 {
			if !sample.SeenUTC.After(now) && now.Sub(sample.SeenUTC) <= e.sampleAge {
				deadFollowers[slot] = true
			}
			continue
		}
		positions[slot] = follow.Position{
			X: sample.X, Y: sample.Y, Seen: sample.SeenUTC, Valid: true,
			Area: strings.ToLower(leaderArea.AreaID) + fmt.Sprintf(":%d", leaderArea.Seed),
		}
	}
	commands := e.coordinator.Step(now, positions)
	for slot, command := range commands {
		if deadFollowers[slot] {
			commands[slot] = follow.Command{State: follow.Waiting, Reason: "dead follower"}
			continue
		}
		sample, exists := e.samples[slot]
		if exists && sample.Life != nil && *sample.Life > 0 && command.State != follow.Waiting {
			command.ManualOverrideEligible = true
			commands[slot] = command
		}
	}
	return commands
}
