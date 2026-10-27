package follow

import (
	"math"
	"time"
)

// CloseMirrorInput is the observed main-controller direction together with the
// follower decision. The caller must supply a verified, fresh game frame.
type CloseMirrorInput struct {
	Follow             Command
	LeftX, LeftY       float64
	Enabled, Blocked   bool
	Duration           time.Duration
	Now, FrameReceived time.Time
	Area               string
	Leader             int
	MirroredSprintHeld bool
	WithinCloseRange   bool
	TeleportRecovery   bool
}

// CloseMirror mirrors a short burst of the main controller's left stick while
// Smart Follow is holding, then asks Smart Follow to continue on its own.
// This mirrors the supplied 0.6.17 controller's state transitions; connecting
// a trusted physical-input stream and the continuation path is a separate gate.
type CloseMirror struct {
	leftWasActive          bool
	active                 bool
	sprintWasHeld          bool
	mirroringSprint        bool
	sprintDistanceReleased bool
	sprintStarted          time.Time
	area                   string
	leader                 int
	until, lastCall        time.Time
	ContinuousFollow       bool
}

func NormalizeMirrorDuration(value time.Duration) time.Duration {
	if value < 50*time.Millisecond {
		return 50 * time.Millisecond
	}
	if value > 5*time.Second {
		return 5 * time.Second
	}
	return value
}

func (m *CloseMirror) Reset() { *m = CloseMirror{} }

func (m *CloseMirror) SuppressMirroredSprint() bool { return m.sprintDistanceReleased }

func (m *CloseMirror) ContinueWith(leftX, leftY float64, allowed bool) bool {
	return allowed && m.ContinuousFollow && leftX*leftX+leftY*leftY > 0.03
}

func (m *CloseMirror) Apply(input CloseMirrorInput) Command {
	follow := input.Follow
	leftActive := input.LeftX*input.LeftX+input.LeftY*input.LeftY > 0.03
	if !input.MirroredSprintHeld {
		m.sprintDistanceReleased = false
	}
	invalid := follow.State == Waiting || input.Area == "" || input.Leader < 1 || input.Leader > 6 ||
		!input.Enabled || input.Blocked ||
		!finiteMirror(input.LeftX) || !finiteMirror(input.LeftY) ||
		input.Now.IsZero() || input.FrameReceived.IsZero() || input.FrameReceived.After(input.Now) ||
		(!leftActive && !(m.mirroringSprint && input.MirroredSprintHeld)) ||
		(input.TeleportRecovery && !(m.mirroringSprint && input.MirroredSprintHeld)) ||
		input.Now.Sub(input.FrameReceived) > 500*time.Millisecond
	if invalid {
		released, area, leader := m.sprintDistanceReleased, m.area, m.leader
		m.Reset()
		m.leftWasActive = leftActive
		m.sprintWasHeld = input.MirroredSprintHeld
		m.sprintDistanceReleased = released
		if released {
			m.area, m.leader = area, leader
		}
		if input.Area != "" {
			m.area, m.leader, m.lastCall = input.Area, input.Leader, input.Now
		}
		return follow
	}
	if m.area != input.Area || m.leader != input.Leader ||
		!m.lastCall.IsZero() && input.Now.Sub(m.lastCall) > 500*time.Millisecond {
		released := m.sprintDistanceReleased && m.area == input.Area && m.leader == input.Leader
		m.Reset()
		m.sprintDistanceReleased = released
		m.area, m.leader = input.Area, input.Leader
	}
	m.lastCall = input.Now
	if m.sprintDistanceReleased {
		m.leftWasActive = leftActive
		m.sprintWasHeld = input.MirroredSprintHeld
		m.ContinuousFollow = true
		return follow
	}
	if input.MirroredSprintHeld {
		if !m.sprintWasHeld || m.sprintStarted.IsZero() {
			m.sprintStarted = input.Now
		}
		if follow.Distance <= 8 && input.Now.Sub(m.sprintStarted) >= 125*time.Millisecond {
			m.sprintDistanceReleased = true
			m.mirroringSprint = false
			m.active = true
			m.until = input.Now
			m.sprintWasHeld = true
			m.leftWasActive = leftActive
			m.ContinuousFollow = true
			return follow
		}
	}
	if input.MirroredSprintHeld && input.WithinCloseRange && (!m.sprintWasHeld || !m.leftWasActive) {
		m.mirroringSprint = true
		m.active = true
		m.ContinuousFollow = false
	}
	m.sprintWasHeld = input.MirroredSprintHeld
	if m.mirroringSprint {
		m.leftWasActive = leftActive
		if input.MirroredSprintHeld {
			follow.X, follow.Y = normalizedMirror(input.LeftX, input.LeftY)
			follow.Reason = "Mirrored dodge / sprint"
			follow.SprintDown = false
			return follow
		}
		m.mirroringSprint = false
		m.until = input.Now
		m.ContinuousFollow = true
		return follow
	}
	if !m.leftWasActive && follow.State == Holding {
		m.active = true
		m.until = input.Now.Add(NormalizeMirrorDuration(input.Duration))
	}
	m.leftWasActive = true
	if !m.active {
		return follow
	}
	if !input.Now.Before(m.until) {
		m.ContinuousFollow = true
		return follow
	}
	if follow.State == Holding {
		follow.X, follow.Y = normalizedMirror(input.LeftX, input.LeftY)
		follow.Reason = "Close-range mirror"
		follow.MirrorMovement = true
	}
	return follow
}

func finiteMirror(value float64) bool { return !math.IsNaN(value) && !math.IsInf(value, 0) }

func normalizedMirror(x, y float64) (float64, float64) {
	length := math.Hypot(x, y)
	if length <= 0 {
		return 0, 0
	}
	return x / length, y / length
}
