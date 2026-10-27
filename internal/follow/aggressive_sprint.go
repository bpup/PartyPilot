package follow

import (
	"math"
	"time"
)

type aggressivePhase uint8

const (
	aggressiveIdle aggressivePhase = iota
	aggressiveStabilizing
	aggressiveHolding
	aggressiveReleaseGap
)

// aggressiveSprint implements the optional observed dodge-to-sprint cycle.
// It is separate from normal sprint and the classic retry controller.
type aggressiveSprint struct {
	phase                aggressivePhase
	phaseSince           time.Time
	lastPressAt          time.Time
	unsafeSince          time.Time
	progressSince        time.Time
	lastSpeedAt          time.Time
	slowSince            time.Time
	stableDirection      point
	pressDirection       point
	progressFollower     point
	lastSpeedFollower    point
	progressSeparation   float64
	speedAverage         float64
	referenceSprintSpeed float64
}

func (s *aggressiveSprint) Reset() { *s = aggressiveSprint{} }

func (s *aggressiveSprint) Update(active, segmentSafe bool, movement, follower point,
	separation float64, at time.Time, retryDelay time.Duration) bool {
	if retryDelay < 500*time.Millisecond {
		retryDelay = 500 * time.Millisecond
	} else if retryDelay > 1500*time.Millisecond {
		retryDelay = 1500 * time.Millisecond
	}
	usable := movement.x*movement.x+movement.y*movement.y >= 0.5
	s.observeSpeed(follower, at)
	if !active {
		s.Reset()
		return false
	}
	switch s.phase {
	case aggressiveIdle:
		s.beginStabilizing(movement, at)
		return false
	case aggressiveReleaseGap:
		if at.Sub(s.phaseSince) < 100*time.Millisecond {
			return false
		}
		s.beginStabilizing(movement, at)
		return false
	case aggressiveStabilizing:
		if !segmentSafe || !usable ||
			s.stableDirection.x*s.stableDirection.x+s.stableDirection.y*s.stableDirection.y < 0.5 ||
			s.stableDirection.x*movement.x+s.stableDirection.y*movement.y < 0.9 {
			s.phaseSince = at
			s.stableDirection = movement
			return false
		}
		s.stableDirection = movement
		if at.Sub(s.phaseSince) < 150*time.Millisecond ||
			!s.lastPressAt.IsZero() && at.Sub(s.lastPressAt) < retryDelay {
			return false
		}
		s.phase = aggressiveHolding
		s.phaseSince, s.lastPressAt = at, at
		s.pressDirection = movement
		s.unsafeSince = time.Time{}
		s.progressFollower, s.progressSeparation, s.progressSince = follower, separation, at
		s.lastSpeedFollower, s.lastSpeedAt = follower, at
		s.speedAverage, s.referenceSprintSpeed = 0, 0
		s.slowSince = time.Time{}
		return true
	}
	if !segmentSafe || !usable {
		if s.unsafeSince.IsZero() {
			s.unsafeSince = at
		}
	} else {
		s.unsafeSince = time.Time{}
	}
	stalled := false
	if at.Sub(s.progressSince) >= 450*time.Millisecond {
		travel := math.Hypot(follower.x-s.progressFollower.x, follower.y-s.progressFollower.y)
		stalled = travel < 3 && s.progressSeparation-separation < 2
		if !stalled {
			s.progressFollower, s.progressSeparation, s.progressSince = follower, separation, at
		}
	}
	majorTurn := usable && s.pressDirection.x*s.pressDirection.x+s.pressDirection.y*s.pressDirection.y >= 0.5 &&
		s.pressDirection.x*movement.x+s.pressDirection.y*movement.y < 0.57
	holdAge := at.Sub(s.phaseSince)
	if holdAge >= 150*time.Millisecond && s.speedAverage > 0 {
		s.referenceSprintSpeed = math.Max(s.referenceSprintSpeed, s.speedAverage)
	}
	slow := holdAge >= 350*time.Millisecond && s.referenceSprintSpeed >= 5 &&
		s.speedAverage < s.referenceSprintSpeed*0.55
	if slow {
		if s.slowSince.IsZero() {
			s.slowSince = at
		}
	} else {
		s.slowSince = time.Time{}
	}
	slowDebounced := !s.slowSince.IsZero() && at.Sub(s.slowSince) >= 300*time.Millisecond
	unsafeDebounced := !s.unsafeSince.IsZero() && at.Sub(s.unsafeSince) >= 100*time.Millisecond
	if holdAge >= 350*time.Millisecond && (majorTurn || unsafeDebounced || stalled || slowDebounced) {
		s.phase, s.phaseSince = aggressiveReleaseGap, at
		s.stableDirection = movement
		return false
	}
	return true
}

func (s *aggressiveSprint) beginStabilizing(direction point, at time.Time) {
	s.phase, s.phaseSince = aggressiveStabilizing, at
	s.stableDirection = direction
	s.unsafeSince = time.Time{}
}

func (s *aggressiveSprint) observeSpeed(follower point, at time.Time) {
	if !s.lastSpeedAt.IsZero() {
		seconds := at.Sub(s.lastSpeedAt).Seconds()
		if seconds >= 0.005 && seconds <= 0.2 {
			speed := math.Hypot(follower.x-s.lastSpeedFollower.x,
				follower.y-s.lastSpeedFollower.y) / seconds
			if s.speedAverage <= 0 {
				s.speedAverage = speed
			} else {
				s.speedAverage = s.speedAverage*0.7 + speed*0.3
			}
		}
	}
	s.lastSpeedFollower, s.lastSpeedAt = follower, at
}
