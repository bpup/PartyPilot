package follow

import (
	"math"
	"time"

	"partypilot/internal/navigation"
)

func (e *Engine) asyncTargetRouteDirection(terrain *navigation.Grid,
	fromX, fromY, targetX, targetY, avoidX, avoidY float64) (float64, float64, navigation.PathState) {
	if segmentDistance(avoidX, avoidY, fromX, fromY, targetX, targetY) >= 5 &&
		terrain.HasLineOfSightAvoid(fromX, fromY, targetX, targetY, avoidX, avoidY, 5, 1) {
		if e.planningTerrain != nil {
			e.clearPlanner()
		}
		e.route = nil
		e.routeTerrain = nil
		e.planningTerrain = nil
		e.planPending = false
		dx, dy := targetX-fromX, targetY-fromY
		length := math.Hypot(dx, dy)
		if length > 0 {
			return dx / length, dy / length, navigation.PathReady
		}
		return 0, 0, navigation.PathUnreachable
	}
	now := time.Now()
	if e.planningTerrain != nil && e.planningTerrain != terrain {
		e.clearPlanner()
	}
	targetDrift := math.Hypot(e.planTarget.x-targetX, e.planTarget.y-targetY)
	originDrift := math.Hypot(e.planOrigin.x-fromX, e.planOrigin.y-fromY)
	avoidDrift := math.Hypot(e.planAvoid.x-avoidX, e.planAvoid.y-avoidY)
	needsPlan := e.planningTerrain != terrain || targetDrift > 3 ||
		originDrift > 8 || avoidDrift > 3
	if e.planPending && now.Sub(e.planRequestedAt) < 250*time.Millisecond &&
		targetDrift <= 16 && originDrift <= 16 && avoidDrift <= 16 &&
		e.planningTerrain == terrain {
		needsPlan = false
	}
	if needsPlan {
		e.planningTerrain = terrain
		e.planOrigin = point{fromX, fromY}
		e.planTarget = point{targetX, targetY}
		e.planAvoid = point{avoidX, avoidY}
		e.planRequestedAt = now
	}
	plan := e.planner.Request(terrain,
		navigation.Waypoint{X: e.planOrigin.x, Y: e.planOrigin.y},
		navigation.Waypoint{X: e.planTarget.x, Y: e.planTarget.y},
		navigation.Waypoint{X: e.planAvoid.x, Y: e.planAvoid.y}, 0)
	e.planPending = plan.State == navigation.PathPending
	if plan.State == navigation.PathReady {
		e.route = plan.Route
		e.routeTerrain = terrain
	}
	if e.routeTerrain != terrain {
		e.route = nil
	}
	e.route = terrain.AdvanceTargetRoute(e.route, fromX, fromY, avoidX, avoidY, 5)
	if len(e.route) > 0 && !terrain.HasClearFollowSegmentAvoid(fromX, fromY,
		e.route[0].X, e.route[0].Y, avoidX, avoidY, 5) {
		e.route = nil
		e.routeTerrain = nil
		e.clearPlanner()
		e.planningTerrain = nil
	}
	if len(e.route) == 0 {
		return 0, 0, plan.State
	}
	dx, dy := e.route[0].X-fromX, e.route[0].Y-fromY
	length := math.Hypot(dx, dy)
	if length == 0 {
		return 0, 0, navigation.PathPending
	}
	return dx / length, dy / length, navigation.PathReady
}
