package live

import (
	"time"

	"partypilot/internal/follow"
	"partypilot/internal/navigation"
)

// SetCloseRangeMirror enables the supplied build's optional two-player stick
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
	if !e.closeMirrorEnabled || len(e.slots) != 2 ||
		e.coordinator.ResolvedTargets()[slot] != e.leader ||
		command.State == follow.Waiting || command.Reason == "manual control" {
		e.resetCloseMirror(slot)
		return command
	}
	leader, leaderOK := positions[e.leader]
	follower, followerOK := positions[slot]
	input, inputOK := e.CurrentMainInput(now)
	if !leaderOK || !followerOK || !inputOK || leader.Area != follower.Area {
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
	command = mirror.Apply(follow.CloseMirrorInput{
		Follow: command, LeftX: input.LeftX, LeftY: input.LeftY,
		Enabled: true, Blocked: input.Blocked,
		Duration: e.closeMirrorDuration, Now: now,
		FrameReceived: e.samples[e.leader].ReceivedAt,
		Area:          leader.Area, Leader: e.leader,
		TeleportRecovery: command.Reason == "Teleport catch-up" ||
			command.Reason == "Teleport terrain path" ||
			command.Reason == "Teleport obstacle recovery",
	})
	if mirror.ContinuousFollow && originalState == follow.Holding && !input.Blocked {
		continued := e.coordinator.ContinueCloseFollow(slot, now, positions, terrains[slot])
		if continued.State == follow.Following || continued.State == follow.CatchingUp {
			continued.X, continued.Y = isometricStick(continued.X, continued.Y)
		}
		if continued.State != follow.Waiting {
			continued.ManualOverrideEligible = command.ManualOverrideEligible
		}
		return continued
	}
	return command
}
