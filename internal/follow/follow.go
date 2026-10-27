package follow

import (
	"math"
	"time"

	"partypilot/internal/navigation"
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
	State                  State   `json:"state"`
	X                      float64 `json:"x"`
	Y                      float64 `json:"y"`
	Distance               float64 `json:"distance"`
	Reason                 string  `json:"reason,omitempty"`
	ManualOverrideEligible bool    `json:"manual_override_eligible,omitempty"`
	SprintDown             bool    `json:"sprint_down,omitempty"`
	MirrorMovement         bool    `json:"mirror_movement,omitempty"`
	SuppressMirroredB      bool    `json:"suppress_mirrored_b,omitempty"`
	MirrorIdle             bool    `json:"mirror_idle,omitempty"`
	AttackDirection        bool    `json:"attack_direction,omitempty"`
}

type Config struct {
	StopDistance               float64
	ResumeDistance             float64
	CatchUpDistance            float64
	MaxAge                     time.Duration
	TrailSpacing               float64
	TrailMaxStep               float64
	RequireTerrain             bool
	TargetTrail                bool
	AsyncTargetPath            bool
	NormalSprintCatchUp        bool
	DisableSprintCatchUp       bool
	ClassicCollisionDodge      bool
	AggressiveBlinkCatchUp     bool
	AggressiveSprintRetryDelay time.Duration
}

func (c Config) Valid() bool {
	return !math.IsInf(c.StopDistance, 0) && !math.IsInf(c.ResumeDistance, 0) &&
		!math.IsInf(c.CatchUpDistance, 0) &&
		!math.IsNaN(c.StopDistance) && !math.IsNaN(c.ResumeDistance) && !math.IsNaN(c.CatchUpDistance) &&
		!math.IsNaN(c.TrailSpacing) && !math.IsNaN(c.TrailMaxStep) &&
		!math.IsInf(c.TrailSpacing, 0) && !math.IsInf(c.TrailMaxStep, 0) &&
		c.StopDistance >= 0 &&
		c.ResumeDistance > c.StopDistance &&
		c.CatchUpDistance > 0 &&
		c.MaxAge > 0 && (!c.AsyncTargetPath || c.TargetTrail) &&
		(!c.AggressiveBlinkCatchUp || (c.NormalSprintCatchUp &&
			c.AggressiveSprintRetryDelay >= 500*time.Millisecond &&
			c.AggressiveSprintRetryDelay <= 1500*time.Millisecond)) &&
		((c.TrailSpacing == 0 && c.TrailMaxStep == 0) ||
			(c.TrailSpacing > 0 && c.TrailMaxStep > c.TrailSpacing))
}

func (e *Engine) Reset() {
	e.moving = false
	e.catchingUp = false
	e.teleportRecovery = false
	e.trail = nil
	e.hasLeader = false
	e.trailArea = ""
	e.trailBlocked = false
	e.terrainSeen = false
	e.initialHeading = point{}
	e.lastLeaderSampleAt = time.Time{}
	e.lastLeaderMotionAt = time.Time{}
	e.leaderSpeedAverage = 0
	e.holdingTarget = false
	e.heldTarget = point{}
	e.stopBubbleHolding = false
	e.sprint.Reset()
	e.aggressive.Reset()
	e.lastSprintObservation = time.Time{}
	e.sprintDown = false
	e.normalSprintDirection = point{}
	e.normalSprintSince = time.Time{}
	e.route = nil
	e.routeTerrain = nil
	e.planningTerrain = nil
	e.planPending = false
	if e.planner != nil && e.plannerOwned {
		e.planner.Clear()
	}
}

type point struct{ x, y float64 }

type Engine struct {
	config                Config
	moving                bool
	catchingUp            bool
	teleportRecovery      bool
	trail                 []point
	lastLeader            point
	hasLeader             bool
	trailArea             string
	trailBlocked          bool
	terrainSeen           bool
	initialHeading        point
	lastLeaderSampleAt    time.Time
	lastLeaderMotionAt    time.Time
	leaderSpeedAverage    float64
	holdingTarget         bool
	heldTarget            point
	stopBubbleHolding     bool
	sprint                classicSprint
	aggressive            aggressiveSprint
	lastSprintObservation time.Time
	sprintDown            bool
	normalSprintDirection point
	normalSprintSince     time.Time
	route                 []navigation.Waypoint
	routeTerrain          *navigation.Grid
	planOrigin            point
	planTarget            point
	planAvoid             point
	planPending           bool
	planRequestedAt       time.Time
	planningTerrain       *navigation.Grid
	planner               *navigation.PathPlanner
	plannerOwned          bool
}

func (e *Engine) StopDistance() float64 { return e.config.StopDistance }

func New(config Config) (*Engine, bool) {
	if !config.Valid() {
		return nil, false
	}
	engine := &Engine{config: config}
	if config.AsyncTargetPath {
		engine.planner = navigation.NewPathPlanner()
		engine.plannerOwned = true
	}
	return engine, true
}

func NewWithPathPlanner(config Config, planner *navigation.PathPlanner) (*Engine, bool) {
	if !config.Valid() || config.AsyncTargetPath != (planner != nil) {
		return nil, false
	}
	return &Engine{config: config, planner: planner}, true
}

func (e *Engine) clearPlanner() {
	if e.planner != nil && e.plannerOwned {
		e.planner.Clear()
	}
}

func (e *Engine) Close() {
	if e.planner != nil && e.plannerOwned {
		e.planner.Close()
	}
}

func (e *Engine) Step(now time.Time, leader, follower Position) Command {
	return e.StepWithTerrain(now, leader, follower, nil)
}

func (e *Engine) StepWithTerrain(now time.Time, leader, follower Position, terrain *navigation.Grid) Command {
	return e.StepWithTerrainCloseFollow(now, leader, follower, terrain, false)
}

// StepWithTerrainCloseFollow re-evaluates a holding follower after the close
// mirror burst ends. The stop bubble can be crossed only behind a recently
// moving leader; normal callers always pass false.
func (e *Engine) StepWithTerrainCloseFollow(now time.Time, leader, follower Position,
	terrain *navigation.Grid, continuousCloseFollow bool) Command {
	wait := func(reason string) Command {
		blocked := e.trailBlocked
		terrainSeen := e.terrainSeen
		e.Reset()
		e.trailBlocked = blocked
		e.terrainSeen = terrainSeen
		return Command{State: Waiting, Reason: reason}
	}
	if terrain != nil {
		e.terrainSeen = true
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
	if (e.config.RequireTerrain || e.terrainSeen) && terrain == nil {
		return wait("terrain missing")
	}
	if terrain != nil && (!terrain.Valid() || terrain.Area != leader.Area) {
		return wait("terrain unavailable or different instance")
	}
	if e.trailBlocked {
		return Command{State: Waiting, Reason: "trail requires reset after leader jump"}
	}
	dx, dy := leader.X-follower.X, leader.Y-follower.Y
	distance := math.Hypot(dx, dy)
	jump := false
	if e.config.TargetTrail {
		jump = e.updateTargetTrail(leader, follower)
	} else if e.config.TrailSpacing > 0 {
		if reason := e.updateTrail(leader); reason != "" {
			e.moving = false
			e.trailBlocked = true
			return Command{State: Waiting, Reason: reason}
		}
	}
	continuousActive := continuousCloseFollow && !jump && distance > 5 &&
		!e.lastLeaderMotionAt.IsZero() && now.Sub(e.lastLeaderMotionAt) >= 0 &&
		now.Sub(e.lastLeaderMotionAt) < 250*time.Millisecond
	if e.config.TargetTrail {
		if e.config.StopDistance <= 0 || continuousActive {
			e.stopBubbleHolding = false
		} else if distance <= e.config.StopDistance {
			e.stopBubbleHolding = true
		} else if e.stopBubbleHolding && distance >= e.config.StopDistance+3 {
			e.stopBubbleHolding = false
		}
	}
	if e.config.TargetTrail && e.stopBubbleHolding ||
		!e.config.TargetTrail && !continuousActive && (distance <= e.config.StopDistance ||
			(!e.moving && distance < e.config.ResumeDistance)) {
		e.moving = false
		e.catchingUp = false
		e.teleportRecovery = false
		e.holdingTarget = false
		e.heldTarget = point{}
		e.sprint.Reset()
		e.aggressive.Reset()
		e.sprintDown = false
		e.lastSprintObservation = time.Time{}
		e.normalSprintDirection = point{}
		e.normalSprintSince = time.Time{}
		e.route = nil
		if e.config.TrailSpacing > 0 {
			e.trail = []point{{leader.X, leader.Y}}
		}
		return Command{State: Holding, Distance: distance}
	}
	entryDistance := math.Max(e.config.StopDistance+1, e.config.CatchUpDistance)
	if e.config.TargetTrail {
		entryDistance = math.Max(entryDistance, 20)
	}
	releaseDistance := e.config.StopDistance
	if e.config.NormalSprintCatchUp || e.config.DisableSprintCatchUp {
		releaseDistance = math.Max(releaseDistance, entryDistance*0.65)
	}
	if jump && distance > releaseDistance {
		e.catchingUp = true
		e.teleportRecovery = true
	} else if !e.catchingUp && distance >= entryDistance {
		e.catchingUp = true
	} else if e.catchingUp && distance <= releaseDistance {
		e.catchingUp = false
	}
	if !e.catchingUp {
		e.teleportRecovery = false
	}
	state := Following
	if e.catchingUp {
		state = CatchingUp
	}
	targetX, targetY := leader.X, leader.Y
	reason := ""
	directTrailClear := false
	if e.config.TargetTrail {
		target := e.trailingTarget(e.config.StopDistance)
		targetX, targetY = target.x, target.y
		if continuousActive {
			e.holdingTarget = false
			if math.Hypot(targetX-follower.X, targetY-follower.Y) <= 2.5 ||
				(targetX-follower.X)*dx+(targetY-follower.Y)*dy <= 0 {
				budget := math.Min(4, distance-5)
				nearest := 0
				nearestDistance := math.Inf(1)
				for index, candidate := range e.trail {
					candidateDistance := math.Hypot(candidate.x-follower.X, candidate.y-follower.Y)
					if candidateDistance < nearestDistance {
						nearest, nearestDistance = index, candidateDistance
					}
				}
				for _, candidate := range e.trail[nearest:] {
					cx, cy := candidate.x-follower.X, candidate.y-follower.Y
					length := math.Hypot(cx, cy)
					if budget > 0.25 && length > 2.5 && cx*dx+cy*dy > 0 {
						targetX = follower.X + cx/length*math.Min(budget, length)
						targetY = follower.Y + cy/length*math.Min(budget, length)
						break
					}
				}
			}
		}
		if e.holdingTarget && math.Hypot(targetX-e.heldTarget.x, targetY-e.heldTarget.y) >= 1.5 {
			e.holdingTarget = false
		}
		threshold := 2.5
		if continuousActive {
			threshold = 0.25
		}
		if !e.holdingTarget &&
			(math.Hypot(targetX-follower.X, targetY-follower.Y) <= threshold || distance <= 3.75) {
			e.holdingTarget = true
			e.heldTarget = target
		}
		if e.holdingTarget {
			e.moving = false
			e.catchingUp = false
			e.teleportRecovery = false
			e.sprint.Reset()
			e.aggressive.Reset()
			e.sprintDown = false
			e.lastSprintObservation = time.Time{}
			e.normalSprintDirection = point{}
			e.normalSprintSince = time.Time{}
			e.route = nil
			return Command{State: Holding, Distance: distance}
		}
		directTrailClear = segmentDistance(leader.X, leader.Y,
			follower.X, follower.Y, targetX, targetY) >= 5 &&
			(terrain == nil || terrain.HasLineOfSightAvoid(follower.X, follower.Y,
				targetX, targetY, leader.X, leader.Y, 5, 1))
		// The supplied build detours around the leader when the direct trail
		// segment passes within five grid units. Live input currently has no
		// verified terrain, so this branch mirrors its terrain-free path.
		if terrain == nil && segmentDistance(leader.X, leader.Y,
			follower.X, follower.Y, targetX, targetY) < 5 {
			targetX, targetY = playerDetour(leader.X, leader.Y,
				follower.X, follower.Y, targetX, targetY)
			reason = "Player avoidance"
		}
	} else if e.config.TrailSpacing > 0 {
		waypoint := e.nextWaypoint(follower)
		targetX, targetY = waypoint.x, waypoint.y
		if targetX == follower.X && targetY == follower.Y {
			targetX, targetY = leader.X, leader.Y
		}
	}
	collisionTargetX, collisionTargetY := targetX, targetY
	if terrain != nil {
		var ok bool
		if e.config.TargetTrail && terrain.TargetNavigation {
			if e.planner != nil {
				var pathState navigation.PathState
				dx, dy, pathState = e.asyncTargetRouteDirection(terrain,
					follower.X, follower.Y, targetX, targetY, leader.X, leader.Y)
				if pathState == navigation.PathPending {
					e.sprintDown = false
					e.normalSprintDirection = point{}
					e.normalSprintSince = time.Time{}
					return Command{State: state, Distance: distance, Reason: "Planning route"}
				}
				ok = pathState == navigation.PathReady
			} else {
				dx, dy, ok = e.targetRouteDirection(terrain,
					follower.X, follower.Y, targetX, targetY, leader.X, leader.Y)
			}
			if !ok {
				recoveryX, recoveryY := playerDetour(leader.X, leader.Y,
					follower.X, follower.Y, targetX, targetY)
				dx, dy = recoveryX-follower.X, recoveryY-follower.Y
				if length := math.Hypot(dx, dy); length > 0 {
					dx, dy, ok = dx/length, dy/length, true
					collisionTargetX, collisionTargetY = recoveryX, recoveryY
					reason = "Obstacle recovery"
				}
			} else if len(e.route) > 0 {
				collisionTargetX, collisionTargetY = e.route[0].X, e.route[0].Y
			}
		} else {
			dx, dy, ok = terrain.NextDirection(follower.X, follower.Y, targetX, targetY)
		}
		if !ok || (dx == 0 && dy == 0) {
			return wait("terrain route unavailable")
		}
	} else if e.config.TargetTrail || e.config.TrailSpacing > 0 {
		dx, dy = targetX-follower.X, targetY-follower.Y
		if length := math.Hypot(dx, dy); length > 0 {
			dx, dy = dx/length, dy/length
		} else {
			dx, dy = (leader.X-follower.X)/distance, (leader.Y-follower.Y)/distance
		}
	} else {
		dx, dy = dx/distance, dy/distance
	}
	e.moving = true
	if e.config.TargetTrail {
		observedAt := leader.Seen
		if follower.Seen.After(observedAt) {
			observedAt = follower.Seen
		}
		if observedAt.After(e.lastSprintObservation) {
			if e.config.DisableSprintCatchUp {
				e.sprint.Reset()
				e.aggressive.Reset()
				e.normalSprintDirection = point{}
				e.normalSprintSince = time.Time{}
				e.sprintDown = false
			} else if e.config.AggressiveBlinkCatchUp {
				e.sprint.Reset()
				e.normalSprintDirection = point{}
				e.normalSprintSince = time.Time{}
				segmentSafe := directTrailClear
				if !segmentSafe && terrain != nil && len(e.route) > 0 {
					waypoint := e.route[0]
					segmentSafe = math.Hypot(waypoint.X-follower.X, waypoint.Y-follower.Y) >= 18 &&
						terrain.HasLineOfSightAvoid(follower.X, follower.Y, waypoint.X, waypoint.Y,
							leader.X, leader.Y, 5, 1)
				}
				e.sprintDown = e.aggressive.Update(e.catchingUp &&
					math.Hypot(targetX-follower.X, targetY-follower.Y) > 30,
					segmentSafe, point{dx, dy}, point{follower.X, follower.Y},
					distance, observedAt, e.config.AggressiveSprintRetryDelay)
			} else if e.config.NormalSprintCatchUp {
				e.aggressive.Reset()
				e.sprint.Reset()
				e.sprintDown = e.updateNormalSprint(dx, dy,
					e.catchingUp && directTrailClear &&
						math.Hypot(targetX-follower.X, targetY-follower.Y) > 30, observedAt)
			} else {
				e.aggressive.Reset()
				e.normalSprintDirection = point{}
				e.normalSprintSince = time.Time{}
				collisionImminent := e.config.ClassicCollisionDodge && terrain != nil &&
					terrain.SprintCollisionImminent(follower.X, follower.Y,
						collisionTargetX, collisionTargetY)
				e.sprintDown = e.sprint.Update(e.catchingUp,
					distance >= e.config.StopDistance+5, collisionImminent,
					point{follower.X, follower.Y}, observedAt)
			}
			e.lastSprintObservation = observedAt
		}
	}
	if e.teleportRecovery {
		switch {
		case reason == "Obstacle recovery":
			reason = "Teleport obstacle recovery"
		case len(e.route) > 0:
			reason = "Teleport terrain path"
		default:
			reason = "Teleport catch-up"
		}
	}
	return Command{
		State:      state,
		X:          dx,
		Y:          dy,
		Distance:   distance,
		Reason:     reason,
		SprintDown: e.sprintDown,
	}
}

func (e *Engine) updateNormalSprint(dx, dy float64, eligible bool, at time.Time) bool {
	if !eligible || dx*dx+dy*dy < 0.5 {
		e.normalSprintDirection = point{}
		e.normalSprintSince = time.Time{}
		return false
	}
	if e.normalSprintSince.IsZero() ||
		e.normalSprintDirection.x*dx+e.normalSprintDirection.y*dy < 0.94 {
		e.normalSprintDirection = point{dx, dy}
		e.normalSprintSince = at
		return false
	}
	e.normalSprintDirection = point{dx, dy}
	return at.Sub(e.normalSprintSince) >= 200*time.Millisecond
}

func segmentDistance(px, py, sx, sy, ex, ey float64) float64 {
	dx, dy := ex-sx, ey-sy
	lengthSquared := dx*dx + dy*dy
	if lengthSquared < 0.001 {
		return math.Hypot(px-sx, py-sy)
	}
	t := math.Max(0, math.Min(1, ((px-sx)*dx+(py-sy)*dy)/lengthSquared))
	return math.Hypot(px-(sx+t*dx), py-(sy+t*dy))
}

func playerDetour(lx, ly, fx, fy, tx, ty float64) (float64, float64) {
	dx, dy := tx-fx, ty-fy
	length := math.Hypot(dx, dy)
	if length*length < 0.001 {
		return tx, ty
	}
	ox, oy := -dy/length*7, dx/length*7
	leftX, leftY := lx+ox, ly+oy
	rightX, rightY := lx-ox, ly-oy
	if math.Hypot(fx-leftX, fy-leftY) <= math.Hypot(fx-rightX, fy-rightY) {
		return leftX, leftY
	}
	return rightX, rightY
}

func (e *Engine) updateTargetTrail(leader, follower Position) bool {
	current := point{leader.X, leader.Y}
	jump := false
	if len(e.trail) > 0 && e.trailArea == leader.Area {
		delta := math.Hypot(current.x-e.lastLeader.x, current.y-e.lastLeader.y)
		if delta*delta > 0.01 && leader.Seen.After(e.lastLeaderSampleAt) {
			e.lastLeaderMotionAt = leader.Seen
		}
		seconds := math.Max(0.001, leader.Seen.Sub(e.lastLeaderSampleAt).Seconds())
		speed := delta / seconds
		jump = delta >= 40 || (delta >= 12 &&
			((e.leaderSpeedAverage > 1 && speed >= e.leaderSpeedAverage*3.5) ||
				(e.leaderSpeedAverage <= 1 && speed >= 400)))
		if !jump && speed > 0 {
			if e.leaderSpeedAverage <= 0 {
				e.leaderSpeedAverage = speed
			} else {
				e.leaderSpeedAverage = e.leaderSpeedAverage*0.9 + speed*0.1
			}
		}
	}
	e.lastLeaderSampleAt = leader.Seen
	if len(e.trail) == 0 || e.trailArea != leader.Area || jump {
		dx, dy := leader.X-follower.X, leader.Y-follower.Y
		length := math.Hypot(dx, dy)
		e.initialHeading = point{1, 0}
		if length > 0.01 {
			e.initialHeading = point{dx / length, dy / length}
		}
		e.trail = []point{current}
		e.lastLeader = current
		e.trailArea = leader.Area
		e.holdingTarget = false
		e.heldTarget = point{}
		if jump {
			e.route = nil
			e.routeTerrain = nil
			e.planningTerrain = nil
			e.clearPlanner()
			e.normalSprintDirection = point{}
			e.normalSprintSince = time.Time{}
		}
		return jump
	}
	e.lastLeader = current
	last := e.trail[len(e.trail)-1]
	if math.Hypot(current.x-last.x, current.y-last.y) >= 1.5 {
		e.trail = append(e.trail, current)
	}
	if len(e.trail) > 1200 {
		e.trail = e.trail[len(e.trail)-1200:]
	}
	return false
}

func (e *Engine) trailingTarget(distance float64) point {
	current := e.lastLeader
	for index := len(e.trail) - 1; index >= 0; index-- {
		previous := e.trail[index]
		segment := math.Hypot(current.x-previous.x, current.y-previous.y)
		if segment <= 0.001 {
			current = previous
			continue
		}
		if distance <= segment {
			fraction := distance / segment
			return point{current.x + (previous.x-current.x)*fraction,
				current.y + (previous.y-current.y)*fraction}
		}
		distance -= segment
		current = previous
	}
	return point{e.trail[0].x - e.initialHeading.x*distance,
		e.trail[0].y - e.initialHeading.y*distance}
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
