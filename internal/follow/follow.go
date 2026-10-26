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
}

func (c Config) Valid() bool {
	return !math.IsInf(c.StopDistance, 0) && !math.IsInf(c.ResumeDistance, 0) &&
		!math.IsInf(c.CatchUpDistance, 0) &&
		c.StopDistance >= 0 &&
		c.ResumeDistance > c.StopDistance &&
		c.CatchUpDistance > c.ResumeDistance &&
		c.MaxAge > 0
}

func (e *Engine) Reset() {
	e.moving = false
}

type Engine struct {
	config Config
	moving bool
}

func New(config Config) (*Engine, bool) {
	if !config.Valid() {
		return nil, false
	}
	return &Engine{config: config}, true
}

func (e *Engine) Step(now time.Time, leader, follower Position) Command {
	wait := func(reason string) Command {
		e.moving = false
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
	dx, dy := leader.X-follower.X, leader.Y-follower.Y
	distance := math.Hypot(dx, dy)
	if distance <= e.config.StopDistance ||
		(!e.moving && distance < e.config.ResumeDistance) {
		e.moving = false
		return Command{State: Holding, Distance: distance}
	}
	e.moving = true
	state := Following
	if distance >= e.config.CatchUpDistance {
		state = CatchingUp
	}
	return Command{
		State:    state,
		X:        dx / distance,
		Y:        dy / distance,
		Distance: distance,
	}
}
