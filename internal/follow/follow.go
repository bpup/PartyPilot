package follow

import (
	"math"
	"time"
)

// Position is an observation from one game client. Area must identify the
// actual instance, not just the map name; the telemetry adapter owns that contract.
type Position struct {
	X, Y  float64
	Area  string
	Seen  time.Time
	Valid bool
}

type State string

const (
	Waiting    State = "waiting"
	Holding    State = "holding"
	Following  State = "following"
	CatchingUp State = "catching_up"
)

type Command struct {
	State    State   `json:"state"`
	X        float64 `json:"x"`
	Y        float64 `json:"y"`
	Distance float64 `json:"distance"`
	Reason   string  `json:"reason,omitempty"`
}

type Config struct {
	StopDistance    float64
	ResumeDistance  float64
	CatchUpDistance float64
	MaxAge          time.Duration
	TrailSpacing    float64
	TrailMaxStep    float64
}

func (c Config) Valid() bool {
	return !math.IsInf(c.StopDistance, 0) && !math.IsInf(c.ResumeDistance, 0) &&
		!math.IsInf(c.CatchUpDistance, 0) &&
		!math.IsNaN(c.StopDistance) && !math.IsNaN(c.ResumeDistance) && !math.IsNaN(c.CatchUpDistance) &&
		!math.IsNaN(c.TrailSpacing) && !math.IsNaN(c.TrailMaxStep) &&
		!math.IsInf(c.TrailSpacing, 0) && !math.IsInf(c.TrailMaxStep, 0) &&
		c.StopDistance >= 0 &&
		c.ResumeDistance > c.StopDistance &&
		c.CatchUpDistance > c.ResumeDistance &&
		c.MaxAge > 0 &&
		((c.TrailSpacing == 0 && c.TrailMaxStep == 0) ||
			(c.TrailSpacing > 0 && c.TrailMaxStep > c.TrailSpacing))
}

func (e *Engine) Reset() {
	e.moving = false
	e.trail = nil
	e.hasLeader = false
	e.trailArea = ""
	e.trailBlocked = false
}

type point struct{ x, y float64 }

type Engine struct {
	config       Config
	moving       bool
	trail        []point
	lastLeader   point
	hasLeader    bool
	trailArea    string
	trailBlocked bool
}

func New(config Config) (*Engine, bool) {
	if !config.Valid() {
		return nil, false
	}
	return &Engine{config: config}, true
}

func (e *Engine) Step(now time.Time, leader, follower Position) Command {
	wait := func(reason string) Command {
		blocked := e.trailBlocked
		e.Reset()
		e.trailBlocked = blocked
		return Command{State: Waiting, Reason: reason}
	}
	if !leader.Valid || !follower.Valid {
		return wait("missing position")
	}
	if leader.Area == "" || follower.Area == "" || leader.Area != follower.Area {
		return wait("different or unknown instance")
	}
	if leader.Seen.After(now) || follower.Seen.After(now) ||
		now.Sub(leader.Seen) > e.config.MaxAge ||
		now.Sub(follower.Seen) > e.config.MaxAge {
		return wait("stale position")
	}
	values := []float64{leader.X, leader.Y, follower.X, follower.Y}
	for _, value := range values {
		if math.IsNaN(value) || math.IsInf(value, 0) {
			return wait("invalid position")
		}
	}
	if e.trailBlocked {
		return Command{State: Waiting, Reason: "trail requires reset after leader jump"}
	}
	dx, dy := leader.X-follower.X, leader.Y-follower.Y
	distance := math.Hypot(dx, dy)
	if e.config.TrailSpacing > 0 {
		if reason := e.updateTrail(leader); reason != "" {
			e.moving = false
			e.trailBlocked = true
			return Command{State: Waiting, Reason: reason}
		}
	}
	if distance <= e.config.StopDistance ||
		(!e.moving && distance < e.config.ResumeDistance) {
		e.moving = false
		if e.config.TrailSpacing > 0 {
			e.trail = []point{{leader.X, leader.Y}}
		}
		return Command{State: Holding, Distance: distance}
	}
	e.moving = true
	if e.config.TrailSpacing > 0 {
		waypoint := e.nextWaypoint(follower)
		dx, dy = waypoint.x-follower.X, waypoint.y-follower.Y
		if length := math.Hypot(dx, dy); length > 0 {
			dx, dy = dx/length, dy/length
		} else {
			dx, dy = (leader.X-follower.X)/distance, (leader.Y-follower.Y)/distance
		}
	} else {
		dx, dy = dx/distance, dy/distance
	}
	state := Following
	if distance >= e.config.CatchUpDistance {
		state = CatchingUp
	}
	return Command{
		State:    state,
		X:        dx,
		Y:        dy,
		Distance: distance,
	}
}

func (e *Engine) updateTrail(leader Position) string {
	current := point{leader.X, leader.Y}
	if !e.hasLeader || e.trailArea != leader.Area {
		e.trail = []point{current}
		e.lastLeader = current
		e.hasLeader = true
		e.trailArea = leader.Area
		return ""
	}
	if math.Hypot(current.x-e.lastLeader.x, current.y-e.lastLeader.y) > e.config.TrailMaxStep {
		return "leader step exceeds trail limit"
	}
	e.lastLeader = current
	last := e.trail[len(e.trail)-1]
	if math.Hypot(current.x-last.x, current.y-last.y) >= e.config.TrailSpacing {
		e.trail = append(e.trail, current)
		if len(e.trail) > 2048 {
			return "leader trail exceeds capacity"
		}
	}
	return ""
}

func (e *Engine) nextWaypoint(follower Position) point {
	radius := e.config.TrailSpacing / 2
	for len(e.trail) > 1 && math.Hypot(e.trail[0].x-follower.X, e.trail[0].y-follower.Y) <= radius {
		e.trail = e.trail[1:]
	}
	return e.trail[0]
}
