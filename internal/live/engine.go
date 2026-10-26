package live

import (
	"fmt"
	"math"
	"sort"
	"strings"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/focus"
	"partypilot/internal/follow"
	"partypilot/internal/navigation"
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
	terrains         map[int]boundTerrain
	leaderName       string
	memberNames      map[int]string
	stopped          string
}

// Target 0.6.17 uses grid coordinates for follow distances. Its distributed
// core maps grid deltas to world deltas with this factor. Render.Pos from the
// independent sensor is a world position; an unknown space is never driven.
const worldPerGridUnit = 10.869565217391305

// The supplied build holds cross-client follow when the two motion frames
// differ by more than 100 ms. SeenUTC is the independent sensor's capture time;
// its separate source TickMs check is not yet available here.
const maxCrossClientMotionSkew = 100 * time.Millisecond

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

func (e *Engine) SetFollowTarget(slot, target int) error {
	return e.coordinator.SetFollowTarget(slot, target)
}

func (e *Engine) SetStopDistance(slot int, distance float64) error {
	return e.coordinator.SetStopDistance(slot, distance)
}

func (e *Engine) Leader() int { return e.leader }

func (e *Engine) Close() { e.coordinator.Close() }

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
	if !exists || sample.SeenUTC.IsZero() || sample.CoordinateSpace != "world" {
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
	terrainWait := make(map[int]bool)
	skewWait := make(map[int]bool)
	terrains := make(map[int]*navigation.Grid)
	for _, slot := range e.slots {
		sample, hasSample := e.samples[slot]
		area, hasArea := e.areas[slot]
		if !hasSample || !hasArea || sample.SeenUTC.Before(area.ObservedAt) {
			continue
		}
		if area.Origin != "live" || area.ObservedAt.After(now) || now.Sub(area.ObservedAt) > e.logAge {
			continue
		}
		if slot != e.leader && sample.Life != nil && *sample.Life <= 0 {
			if !sample.SeenUTC.After(now) && now.Sub(sample.SeenUTC) <= e.sampleAge {
				deadFollowers[slot] = true
			}
			continue
		}
		positions[slot] = follow.Position{
			X: sample.X / worldPerGridUnit, Y: sample.Y / worldPerGridUnit,
			Seen: sample.SeenUTC, Valid: true,
			Area: strings.ToLower(area.AreaID) + fmt.Sprintf(":%d", area.Seed),
		}
		if bound, configured := e.terrains[slot]; configured {
			if sample.AreaHash != bound.binding.AreaHash || sample.SeenUTC.Before(bound.seen) ||
				!strings.EqualFold(area.AreaID, bound.binding.AreaID) || area.Seed != bound.binding.Seed ||
				!bound.grid.ContainsPoint(positions[slot].X, positions[slot].Y) {
				delete(positions, slot)
				terrainWait[slot] = true
			} else {
				terrains[slot] = bound.grid
			}
		}
	}
	for slot := range e.terrains {
		if _, ok := terrains[slot]; !ok {
			terrainWait[slot] = true
			continue
		}
		bound := e.terrains[slot]
		for other, position := range positions {
			if position.Area == bound.grid.Area &&
				(e.terrains[other].grid == nil ||
					e.samples[other].AreaHash != bound.binding.AreaHash ||
					!bound.grid.ContainsPoint(position.X, position.Y)) {
				terrainWait[slot] = true
				delete(terrains, slot)
				break
			}
		}
	}
	if len(e.terrains) > 0 {
		for _, slot := range e.slots {
			if slot != e.leader && e.terrains[slot].grid == nil {
				terrainWait[slot] = true
			}
		}
	}
	for slot, target := range e.coordinator.ResolvedTargets() {
		if slot == e.leader {
			continue
		}
		follower, followerOK := positions[slot]
		leader, leaderOK := positions[target]
		if followerOK && leaderOK {
			skew := follower.Seen.Sub(leader.Seen)
			if skew > maxCrossClientMotionSkew || skew < -maxCrossClientMotionSkew {
				skewWait[slot] = true
			}
		}
	}
	holds := make(map[int]string, len(terrainWait)+len(skewWait))
	for slot := range terrainWait {
		holds[slot] = "terrain binding evidence missing"
	}
	for slot := range skewWait {
		holds[slot] = "cross-client motion skew"
	}
	commands := e.coordinator.StepWithTerrainsAndHolds(now, positions, terrains, holds)
	for slot, command := range commands {
		if deadFollowers[slot] {
			commands[slot] = follow.Command{State: follow.Waiting, Reason: "dead follower"}
			continue
		}
		sample, exists := e.samples[slot]
		if command.State == follow.Following || command.State == follow.CatchingUp {
			command.X, command.Y = isometricStick(command.X, command.Y)
		}
		if exists && sample.Life != nil && *sample.Life > 0 && command.State != follow.Waiting {
			command.ManualOverrideEligible = true
		}
		commands[slot] = command
	}
	return commands
}

func isometricStick(gridX, gridY float64) (float64, float64) {
	x, y := gridX-gridY, gridX+gridY
	if length := math.Hypot(x, y); length > 0 {
		return x / length, y / length
	}
	return 0, 0
}
