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
	coordinator           *party.Coordinator
	leader                int
	baseLeader            int
	lastFocusedLeads      bool
	logAge                time.Duration
	sampleAge             time.Duration
	pidToSlot             map[int]int
	pidToRoles            map[int][]RoleBinding
	orderedIdentities     map[int]orderedIdentity
	slotToPID             map[int]int
	slots                 []int
	samples               map[int]telemetry.Sample
	vitals                map[int]telemetry.Sample
	strictVitals          map[int]bool
	areas                 map[int]arealog.Signal
	terrains              map[int]boundTerrain
	terrainStable         map[int]terrainStability
	leaderName            string
	memberNames           map[int]string
	stopped               string
	followPaused          bool
	mainInput             MainInput
	mainInputValid        bool
	acceptMainInput       bool
	closeMirrorEnabled    bool
	closeMirrorDuration   time.Duration
	closeMirrors          map[int]*follow.CloseMirror
	closeMirrorBAllowed   map[int]bool
	mirrorAttackDirection bool
	catchUpPriority       bool
}

// SetFollowPaused temporarily releases movement without discarding process,
// area, or player observations. A latched party stop still takes precedence.
func (e *Engine) SetFollowPaused(paused bool) {
	e.followPaused = paused
	if paused {
		e.mainInputValid = false
		e.resetCloseMirrors()
	}
}
func (e *Engine) FollowPaused() bool { return e.followPaused }

// Target 0.6.17 uses grid coordinates for follow distances. Its distributed
// core maps grid deltas to world deltas with this factor. Render.Pos from the
// independent sensor is a world position; an unknown space is never driven.
const worldPerGridUnit = 10.869565217391305

// The supplied build holds cross-client follow when source ticks or receipt
// timestamps differ by more than 100 ms.
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
	e.mainInputValid = false
	e.resetCloseMirrors()
	return nil
}

func (e *Engine) Stop(reason string) {
	if e.stopped == "" {
		e.stopped = reason
	}
	e.coordinator.ResetFollow()
	e.terrainStable = make(map[int]terrainStability)
	e.mainInputValid = false
	e.resetCloseMirrors()
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

// ObserveClientParty applies an event from one verified client log. The log
// belongs to the process, while a dual-local client owns two party roles.
func (e *Engine) ObserveClientParty(processID int, event partylog.Event) bool {
	roles := e.pidToRoles[processID]
	if len(roles) == 0 {
		return false
	}
	event.ClientSlot = roles[0].Slot
	return e.ObserveParty(event)
}

// ObserveClientLogLine parses one complete line from a process-bound Client.txt.
// For a dual-local client the one area generation updates both roles.
func (e *Engine) ObserveClientLogLine(processID int, line string, observedAt time.Time) {
	roles := e.pidToRoles[processID]
	if len(roles) == 0 {
		return
	}
	if signal, ok := arealog.ParseGeneration(line, roles[0].Slot, observedAt); ok {
		e.ObserveClientArea(processID, signal)
	}
	if event, ok := partylog.Parse(line, roles[0].Slot); ok {
		e.ObserveClientParty(processID, event)
	}
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
	bindings := make([]RoleBinding, 0, len(roster))
	for index, slot := range slots {
		bindings = append(bindings, RoleBinding{Slot: slot, Client: index + 1,
			LocalPlayer: 1, ProcessID: roster[slot]})
	}
	return NewWithBindings(bindings, leader, config, logAge)
}

// NewWithBindings retains the target layout's distinction between a client
// process and its one or two local players. It does not infer local order from
// visible entities; only ObserveOrderedClient can supply a two-player frame.
func NewWithBindings(bindings []RoleBinding, leader int, config follow.Config,
	logAge time.Duration) (*Engine, error) {
	if len(bindings) < 2 || len(bindings) > 6 || logAge <= 0 {
		return nil, fmt.Errorf("live roster needs 2–6 roles and positive log age")
	}
	slots := make([]int, 0, len(bindings))
	players := make([]focus.Player, 0, len(bindings))
	pidToSlot := make(map[int]int)
	pidToRoles := make(map[int][]RoleBinding)
	slotToPID := make(map[int]int)
	clientPIDs := make(map[int]int)
	pidClients := make(map[int]int)
	localSlots := make(map[int]map[int]bool)
	for _, binding := range bindings {
		if binding.Slot < 1 || binding.Slot > 6 || binding.Client < 1 || binding.Client > 3 ||
			binding.LocalPlayer < 1 || binding.LocalPlayer > 2 || binding.ProcessID <= 0 ||
			slotToPID[binding.Slot] != 0 ||
			(clientPIDs[binding.Client] != 0 && clientPIDs[binding.Client] != binding.ProcessID) ||
			(pidClients[binding.ProcessID] != 0 && pidClients[binding.ProcessID] != binding.Client) {
			return nil, fmt.Errorf("invalid role/client/process binding")
		}
		if localSlots[binding.Client] == nil {
			localSlots[binding.Client] = make(map[int]bool)
		}
		if localSlots[binding.Client][binding.LocalPlayer] {
			return nil, fmt.Errorf("duplicate local player in client %d", binding.Client)
		}
		localSlots[binding.Client][binding.LocalPlayer] = true
		clientPIDs[binding.Client] = binding.ProcessID
		pidClients[binding.ProcessID] = binding.Client
		slotToPID[binding.Slot] = binding.ProcessID
		pidToRoles[binding.ProcessID] = append(pidToRoles[binding.ProcessID], binding)
		slots = append(slots, binding.Slot)
		players = append(players, focus.Player{Slot: binding.Slot, Client: binding.Client})
	}
	for client := 1; client <= len(clientPIDs); client++ {
		if clientPIDs[client] == 0 || !localSlots[client][1] ||
			(len(localSlots[client]) == 2 && !localSlots[client][2]) {
			return nil, fmt.Errorf("incomplete local-player order for client %d", client)
		}
	}
	for _, roles := range pidToRoles {
		if len(roles) == 1 {
			pidToSlot[roles[0].ProcessID] = roles[0].Slot
		}
	}
	sort.Ints(slots)
	coordinator, err := party.New(players, leader, config)
	if err != nil {
		return nil, err
	}
	return &Engine{coordinator: coordinator, leader: leader, baseLeader: leader,
		logAge: logAge, sampleAge: config.MaxAge, memberNames: make(map[int]string),
		pidToSlot: pidToSlot, pidToRoles: pidToRoles,
		orderedIdentities: make(map[int]orderedIdentity), slotToPID: slotToPID,
		slots: slots, samples: make(map[int]telemetry.Sample),
		vitals: make(map[int]telemetry.Sample), strictVitals: make(map[int]bool),
		areas: make(map[int]arealog.Signal)}, nil
}

func (e *Engine) ObserveSample(sample telemetry.Sample) bool {
	slot, exists := e.pidToSlot[sample.ProcessID]
	if !exists || !validMovementSample(sample) || !e.sampleIsNew(slot, sample) {
		return false
	}
	e.storeSample(slot, sample)
	return true
}

func validMovementSample(sample telemetry.Sample) bool {
	return sample.SeenUTC.IsZero() == false && sample.TickMS > 0 &&
		!sample.ReceivedAt.IsZero() && sample.CoordinateSpace == "world" &&
		finitePosition(sample.X) && finitePosition(sample.Y) && finitePosition(sample.Z)
}

func (e *Engine) sampleIsNew(slot int, sample telemetry.Sample) bool {
	previous, exists := e.samples[slot]
	return !exists || !sample.SeenUTC.Before(previous.SeenUTC) &&
		sample.TickMS >= previous.TickMS && !sample.ReceivedAt.Before(previous.ReceivedAt)
}

func (e *Engine) storeSample(slot int, sample telemetry.Sample) {
	if bound, exists := e.terrains[slot]; exists &&
		(sample.AreaHash != bound.binding.AreaHash || sample.SeenUTC.Before(bound.seen)) {
		delete(e.terrainStable, slot)
	}
	e.samples[slot] = sample
	if prior, exists := e.vitals[slot]; sample.Life != nil &&
		(!exists || !sample.SeenUTC.Before(prior.SeenUTC) && sample.TickMS >= prior.TickMS) {
		e.vitals[slot] = sample
		delete(e.strictVitals, slot)
	}
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
		(signal.ObservedAt.After(previous.ObservedAt) ||
			!strings.EqualFold(previous.AreaID, signal.AreaID) || previous.Seed != signal.Seed) {
		e.coordinator.ResetFollow()
		delete(e.terrainStable, signal.Slot)
		delete(e.vitals, signal.Slot)
		delete(e.strictVitals, signal.Slot)
	}
	e.areas[signal.Slot] = signal
	return true
}

// ObserveClientArea fans one verified client-log generation out to every
// local role owned by its process. It does not infer instance identity from
// position samples or visible entities.
func (e *Engine) ObserveClientArea(processID int, signal arealog.Signal) bool {
	roles := e.pidToRoles[processID]
	if len(roles) == 0 || signal.AreaID == "" || signal.Seed <= 0 || signal.ObservedAt.IsZero() {
		return false
	}
	for _, role := range roles {
		if previous, exists := e.areas[role.Slot]; exists &&
			signal.ObservedAt.Before(previous.ObservedAt) {
			return false
		}
	}
	for _, role := range roles {
		roleSignal := signal
		roleSignal.Slot = role.Slot
		e.ObserveArea(roleSignal)
	}
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
	if e.followPaused {
		commands := make(map[int]follow.Command, len(e.slots)-1)
		for _, slot := range e.slots {
			if slot != e.leader {
				commands[slot] = follow.Command{State: follow.Waiting, Reason: "follow paused"}
			}
		}
		return commands
	}
	positions := make(map[int]follow.Position, len(e.slots))
	lives := make(map[int]*float64, len(e.slots))
	deadFollowers := make(map[int]bool)
	terrainWait := make(map[int]bool)
	terrainSettling := make(map[int]bool)
	skewWait := make(map[int]bool)
	terrains := make(map[int]*navigation.Grid)
	for _, slot := range e.slots {
		sample, hasSample := e.samples[slot]
		area, hasArea := e.areas[slot]
		if !hasSample || !hasArea || sample.SeenUTC.Before(area.ObservedAt) ||
			sample.ReceivedAt.After(now) || now.Sub(sample.ReceivedAt) > e.sampleAge {
			delete(e.terrainStable, slot)
			continue
		}
		if area.Origin != "live" || area.ObservedAt.After(now) || now.Sub(area.ObservedAt) > e.logAge {
			delete(e.terrainStable, slot)
			continue
		}
		life := e.currentLife(slot, sample, area, now)
		lives[slot] = life
		if slot != e.leader && life != nil && *life <= 0 {
			delete(e.terrainStable, slot)
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
				delete(e.terrainStable, slot)
				delete(positions, slot)
				terrainWait[slot] = true
			} else {
				stability := e.terrainStable[slot]
				if stability.observe(positions[slot].Area, sample.TickMS) {
					terrains[slot] = bound.grid
				} else {
					terrainWait[slot] = true
					terrainSettling[slot] = true
				}
				e.terrainStable[slot] = stability
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
					terrains[other] == nil ||
					e.samples[other].AreaHash != bound.binding.AreaHash ||
					!bound.grid.ContainsPoint(position.X, position.Y)) {
				terrainWait[slot] = true
				if e.terrains[other].grid != nil && terrains[other] == nil &&
					e.terrainStable[other].frames < 3 {
					terrainSettling[slot] = true
				}
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
		_, followerOK := positions[slot]
		_, leaderOK := positions[target]
		if followerOK && leaderOK {
			follower := e.samples[slot]
			leader := e.samples[target]
			receiptSkew := follower.ReceivedAt.Sub(leader.ReceivedAt)
			tickSkew := follower.TickMS - leader.TickMS
			if receiptSkew > maxCrossClientMotionSkew || receiptSkew < -maxCrossClientMotionSkew ||
				tickSkew > 100 || tickSkew < -100 {
				skewWait[slot] = true
			}
		}
	}
	holds := make(map[int]string, len(terrainWait)+len(skewWait))
	for slot := range terrainWait {
		if terrainSettling[slot] {
			holds[slot] = "terrain still settling"
		} else {
			holds[slot] = "terrain binding evidence missing"
		}
	}
	for slot := range skewWait {
		if holds[slot] == "" {
			holds[slot] = "cross-client motion skew"
		}
	}
	commands := e.coordinator.StepWithTerrainsAndHolds(now, positions, terrains, holds)
	for slot, command := range commands {
		if deadFollowers[slot] {
			e.resetCloseMirror(slot)
			commands[slot] = follow.Command{State: follow.Waiting, Reason: "dead follower"}
			continue
		}
		if command.State == follow.Following || command.State == follow.CatchingUp {
			command.X, command.Y = isometricStick(command.X, command.Y)
		}
		if life := lives[slot]; life != nil && *life > 0 && command.State != follow.Waiting {
			command.ManualOverrideEligible = true
		}
		command = e.applyCloseMirror(slot, command, now, positions, terrains)
		if command.State == follow.Holding || command.State == follow.Following ||
			command.State == follow.CatchingUp {
			if input, ok := e.CurrentMainInput(now); ok && !input.Blocked && input.AttackActive {
				if !(command.State == follow.CatchingUp && e.catchUpPriority) {
					command.SprintDown = false
					if e.mirrorAttackDirection {
						length := math.Hypot(input.LeftX, input.LeftY)
						if length*length > 0.03 {
							command.X, command.Y = input.LeftX/length, input.LeftY/length
							command.Reason = "Attack direction"
							command.AttackDirection = true
							command.MirrorMovement = false
							command.MirrorIdle = false
						}
					}
				}
			}
		}
		commands[slot] = command
	}
	return commands
}

func (e *Engine) currentLife(slot int, motion telemetry.Sample, area arealog.Signal,
	now time.Time) *float64 {
	vital, exists := e.vitals[slot]
	if !exists || vital.Life == nil || vital.SeenUTC.Before(area.ObservedAt) ||
		vital.SeenUTC.After(now) || vital.ReceivedAt.After(now) ||
		now.Sub(vital.ReceivedAt) > e.sampleAge ||
		(e.strictVitals[slot] && (motion.AreaHash == 0 || vital.AreaHash == 0)) ||
		(motion.AreaHash != 0 && vital.AreaHash != 0 && motion.AreaHash != vital.AreaHash) {
		return nil
	}
	return vital.Life
}

// flaskActionReady binds a separate-client resource reading to a fresh
// process, motion frame and live area generation before it can request input.
func (e *Engine) flaskActionReady(slot int, now time.Time) bool {
	if e.stopped != "" {
		return false
	}
	motion, hasMotion := e.samples[slot]
	vital, hasVital := e.vitals[slot]
	area, hasArea := e.areas[slot]
	if !hasMotion || !hasVital || !hasArea || area.Origin != "live" ||
		area.ObservedAt.After(now) || now.Sub(area.ObservedAt) > e.logAge ||
		motion.SeenUTC.Before(area.ObservedAt) || motion.SeenUTC.After(now) ||
		motion.ReceivedAt.After(now) || now.Sub(motion.ReceivedAt) > e.sampleAge ||
		vital.SeenUTC.Before(area.ObservedAt) || vital.SeenUTC.After(now) ||
		vital.ReceivedAt.After(now) || now.Sub(vital.ReceivedAt) > e.sampleAge ||
		motion.AreaHash == 0 || vital.AreaHash == 0 ||
		motion.AreaHash != vital.AreaHash || vital.Life == nil || *vital.Life <= 0 {
		return false
	}
	return true
}

func (e *Engine) flaskSupportPairReady(drinker, target int, now time.Time) bool {
	if drinker == target || !e.flaskActionReady(drinker, now) ||
		!e.flaskActionReady(target, now) {
		return false
	}
	drinkerArea := e.areas[drinker]
	targetArea := e.areas[target]
	return strings.EqualFold(drinkerArea.AreaID, targetArea.AreaID) &&
		drinkerArea.Seed == targetArea.Seed
}

// orderedVitalActionReady requires a fresh, area-bound motion and resource
// observation for every local role before a slow frame can request input.
func (e *Engine) orderedVitalActionReady(processID int, now time.Time) bool {
	roles := e.pidToRoles[processID]
	if len(roles) == 0 || e.stopped != "" {
		return false
	}
	for _, role := range roles {
		motion, hasMotion := e.samples[role.Slot]
		vital, hasVital := e.vitals[role.Slot]
		area, hasArea := e.areas[role.Slot]
		if !hasMotion || !hasVital || !hasArea || area.Origin != "live" ||
			area.ObservedAt.After(now) || now.Sub(area.ObservedAt) > e.logAge ||
			motion.SeenUTC.Before(area.ObservedAt) || motion.SeenUTC.After(now) ||
			motion.ReceivedAt.After(now) || now.Sub(motion.ReceivedAt) > e.sampleAge ||
			vital.SeenUTC.Before(area.ObservedAt) || vital.SeenUTC.After(now) ||
			vital.ReceivedAt.After(now) || now.Sub(vital.ReceivedAt) > e.sampleAge ||
			motion.AreaHash == 0 || vital.AreaHash == 0 ||
			motion.AreaHash != vital.AreaHash || vital.Life == nil {
			return false
		}
	}
	return true
}

func isometricStick(gridX, gridY float64) (float64, float64) {
	x, y := gridX-gridY, gridX+gridY
	if length := math.Hypot(x, y); length > 0 {
		return x / length, y / length
	}
	return 0, 0
}
