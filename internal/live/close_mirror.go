package live

import (
	"fmt"
	"strings"
	"time"

	"partypilot/internal/follow"
	"partypilot/internal/navigation"
)

func (e *Engine) SetCloseMirrorBAllowed(slot int, allowed bool) error {
	known := false
	for _, candidate := range e.slots {
		known = known || candidate == slot
	}
	if !known {
		return fmt.Errorf("unknown close-mirror player %d", slot)
	}
	if e.closeMirrorBAllowed == nil {
		e.closeMirrorBAllowed = make(map[int]bool)
	}
	e.closeMirrorBAllowed[slot] = allowed
	return nil
}

// SetCloseRangeMirror enables the supplied build's optional per-follower stick
// burst. A trusted main-input subscription must be enabled separately.
func (e *Engine) SetCloseRangeMirror(enabled bool, duration time.Duration) {
	e.closeMirrorEnabled = enabled
	e.closeMirrorDuration = follow.NormalizeMirrorDuration(duration)
	if !enabled {
		e.resetCloseMirrors()
	}
}

func (e *Engine) resetCloseMirror(slot int) {
	if mirror := e.closeMirrors[slot]; mirror != nil {
		mirror.Reset()
	}
}

func (e *Engine) resetCloseMirrors() {
	for _, mirror := range e.closeMirrors {
		mirror.Reset()
	}
}

func (e *Engine) applyCloseMirror(slot int, command follow.Command, now time.Time,
	positions map[int]follow.Position, terrains map[int]*navigation.Grid) follow.Command {
	if !e.closeMirrorEnabled ||
		e.coordinator.ResolvedTargets()[slot] != e.leader ||
		command.State == follow.Waiting || command.Reason == "manual control" {
		e.resetCloseMirror(slot)
		return command
	}
	leader, leaderOK := positions[e.leader]
	follower, followerOK := positions[slot]
	input, inputOK := e.CurrentMainInput(now)
	if !leaderOK || !followerOK || !inputOK || !strings.EqualFold(leader.Area, follower.Area) {
		e.resetCloseMirror(slot)
		return command
	}
	if e.closeMirrors == nil {
		e.closeMirrors = make(map[int]*follow.CloseMirror)
	}
	mirror := e.closeMirrors[slot]
	if mirror == nil {
		mirror = &follow.CloseMirror{}
		e.closeMirrors[slot] = mirror
	}
	originalState := command.State
	bAllowed := e.closeMirrorBAllowed[slot]
	command = mirror.Apply(follow.CloseMirrorInput{
		Follow: command, LeftX: input.LeftX, LeftY: input.LeftY,
		Enabled: true, Blocked: input.Blocked || input.BDown && !bAllowed,
		Duration: e.closeMirrorDuration, Now: now,
		FrameReceived: e.samples[e.leader].ReceivedAt,
		Area:          leader.Area, Leader: e.leader,
		MirroredSprintHeld: input.BDown && bAllowed,
		WithinCloseRange:   command.Distance <= e.coordinator.StopDistanceFor(slot)+3,
		TeleportRecovery: command.Reason == "Teleport catch-up" ||
			command.Reason == "Teleport terrain path" ||
			command.Reason == "Teleport obstacle recovery",
	})
	command.SuppressMirroredB = mirror.SuppressMirroredSprint()
	if command.SuppressMirroredB {
		command.SprintDown = false
	}
	if mirror.ContinuousFollow && originalState == follow.Holding && !input.Blocked {
		continued := e.coordinator.ContinueCloseFollow(slot, now, positions, terrains[slot])
		if continued.State == follow.Following || continued.State == follow.CatchingUp {
			continued.X, continued.Y = isometricStick(continued.X, continued.Y)
		}
		if continued.State != follow.Waiting {
			continued.ManualOverrideEligible = command.ManualOverrideEligible
			continued.SuppressMirroredB = command.SuppressMirroredB
			if continued.SuppressMirroredB {
				continued.SprintDown = false
			}
		}
		return continued
	}
	return command
}
