package follow

import (
	"math"
	"time"
)

// classicSprint mirrors the supplied build's sprint engagement controller.
// Collision input is available in controlled terrain replay. Live following
// still has no verified terrain source.
type classicSprint struct {
	phase                 sprintPhase
	phaseSince            time.Time
	lastRetry             time.Time
	lastSpeed             time.Time
	slowSince             time.Time
	collisionSince        time.Time
	collisionClearSince   time.Time
	lastFollower          point
	speedAverage          float64
	peakSpeedAverage      float64
	referenceSpeed        float64
	collisionLatched      bool
	verified              bool
	attempt               int
	lastInactiveSpeed     time.Time
	lastInactiveFollower  point
	walkingReferenceSpeed float64
}

type sprintPhase uint8

const (
	sprintIdle sprintPhase = iota
	sprintHolding
	sprintReleaseGap
	sprintWalkingFallback
)

func (s *classicSprint) Reset() { *s = classicSprint{} }

func (s *classicSprint) Update(active, rearmAllowed, collisionImminent bool,
	follower point, at time.Time) bool {
	if !active {
		s.observeWalking(follower, at)
		s.resetEngagement()
		return false
	}
	s.lastInactiveSpeed = time.Time{}
	s.updateSpeed(follower, at)
	if s.phase == sprintIdle {
		s.attempt = 0
		s.lastRetry = at
		s.beginHolding(follower, at)
		return true
	}
	if s.phase == sprintWalkingFallback {
		return false
	}
	if s.phase == sprintReleaseGap {
		if at.Sub(s.phaseSince) < 100*time.Millisecond {
			return false
		}
		s.beginHolding(follower, at)
		return true
	}
	if collisionImminent {
		s.collisionClearSince = time.Time{}
		if !s.collisionLatched && s.collisionSince.IsZero() {
			s.collisionSince = at
		}
	} else {
		s.collisionSince = time.Time{}
		if s.collisionLatched {
			if s.collisionClearSince.IsZero() {
				s.collisionClearSince = at
			}
			if at.Sub(s.collisionClearSince) >= 300*time.Millisecond {
				s.collisionLatched = false
				s.collisionClearSince = time.Time{}
			}
		}
	}
	phaseAge := at.Sub(s.phaseSince)
	if s.speedAverage > 0 {
		s.peakSpeedAverage = math.Max(s.peakSpeedAverage, s.speedAverage)
	}
	mayRetry := at.Sub(s.lastRetry) >= 1200*time.Millisecond
	if !s.verified && phaseAge >= 800*time.Millisecond {
		minimum := 36.0
		if s.walkingReferenceSpeed >= 5 {
			minimum = s.walkingReferenceSpeed * 1.15
		}
		if s.peakSpeedAverage < minimum {
			if !rearmAllowed || s.attempt >= 3 {
				s.phase = sprintWalkingFallback
				s.phaseSince = at
				s.slowSince = time.Time{}
				return false
			}
			if mayRetry {
				s.beginReleaseGap(at, false)
				return false
			}
			return true
		}
		s.verified = true
		s.referenceSpeed = math.Max(s.peakSpeedAverage, s.speedAverage)
	}
	collisionDebounced := !s.collisionLatched && !s.collisionSince.IsZero() &&
		at.Sub(s.collisionSince) >= 100*time.Millisecond
	if rearmAllowed && collisionDebounced && mayRetry {
		s.beginReleaseGap(at, true)
		s.collisionSince = time.Time{}
		s.collisionLatched = true
		s.slowSince = time.Time{}
		return false
	}
	if s.verified && phaseAge >= 650*time.Millisecond && s.speedAverage > 0 {
		s.referenceSpeed = math.Max(s.speedAverage, s.referenceSpeed*0.995)
	}
	slow := s.verified && phaseAge >= 650*time.Millisecond &&
		s.referenceSpeed >= 5 && s.speedAverage < s.referenceSpeed*0.82
	if slow {
		if s.slowSince.IsZero() {
			s.slowSince = at
		}
	} else {
		s.slowSince = time.Time{}
	}
	if rearmAllowed && !s.slowSince.IsZero() &&
		at.Sub(s.slowSince) >= 350*time.Millisecond && mayRetry {
		s.beginReleaseGap(at, true)
		s.slowSince = time.Time{}
		return false
	}
	return true
}

func (s *classicSprint) resetEngagement() {
	walkingAt, walkingFollower, walkingSpeed := s.lastInactiveSpeed,
		s.lastInactiveFollower, s.walkingReferenceSpeed
	*s = classicSprint{lastInactiveSpeed: walkingAt,
		lastInactiveFollower: walkingFollower, walkingReferenceSpeed: walkingSpeed}
}

func (s *classicSprint) beginHolding(follower point, at time.Time) {
	s.attempt = min(3, s.attempt+1)
	s.phase = sprintHolding
	s.phaseSince = at
	s.lastFollower = follower
	s.lastSpeed = at
	s.slowSince = time.Time{}
	s.speedAverage, s.peakSpeedAverage, s.referenceSpeed = 0, 0, 0
	s.verified = false
}

func (s *classicSprint) beginReleaseGap(at time.Time, resetAttempts bool) {
	if resetAttempts {
		s.attempt = 0
	}
	s.phase = sprintReleaseGap
	s.phaseSince = at
	s.lastRetry = at
	s.verified = false
}

func (s *classicSprint) updateSpeed(follower point, at time.Time) {
	if !s.lastSpeed.IsZero() {
		seconds := at.Sub(s.lastSpeed).Seconds()
		if seconds >= 0.005 && seconds <= 0.2 {
			speed := math.Hypot(follower.x-s.lastFollower.x,
				follower.y-s.lastFollower.y) / seconds
			if s.speedAverage <= 0 {
				s.speedAverage = speed
			} else {
				s.speedAverage = s.speedAverage*0.7 + speed*0.3
			}
		}
	}
	s.lastFollower, s.lastSpeed = follower, at
}

func (s *classicSprint) observeWalking(follower point, at time.Time) {
	if !s.lastInactiveSpeed.IsZero() {
		seconds := at.Sub(s.lastInactiveSpeed).Seconds()
		if seconds >= 0.005 && seconds <= 0.2 {
			speed := math.Hypot(follower.x-s.lastInactiveFollower.x,
				follower.y-s.lastInactiveFollower.y) / seconds
			if speed >= 5 {
				if s.walkingReferenceSpeed <= 0 {
					s.walkingReferenceSpeed = speed
				} else {
					s.walkingReferenceSpeed = s.walkingReferenceSpeed*0.85 + speed*0.15
				}
			}
		}
	}
	s.lastInactiveFollower, s.lastInactiveSpeed = follower, at
}
