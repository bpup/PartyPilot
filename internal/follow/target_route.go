package follow

import (
	"math"

	"partypilot/internal/navigation"
)

// targetRouteDirection reuses a target-format route while its origin, goal and
// leader exclusion stay within the supplied build's replan thresholds.
func (e *Engine) targetRouteDirection(terrain *navigation.Grid,
	fromX, fromY, targetX, targetY, avoidX, avoidY float64) (float64, float64, bool) {
	if segmentDistance(avoidX, avoidY, fromX, fromY, targetX, targetY) >= 5 &&
		terrain.HasLineOfSightAvoid(fromX, fromY, targetX, targetY, avoidX, avoidY, 5, 1) {
		e.route = nil
		e.routeTerrain = nil
		dx, dy := targetX-fromX, targetY-fromY
		if length := math.Hypot(dx, dy); length > 0 {
			return dx / length, dy / length, true
		}
		return 0, 0, false
	}
	needsPlan := e.routeTerrain != terrain || len(e.route) == 0 ||
		math.Hypot(e.planTarget.x-targetX, e.planTarget.y-targetY) > 3 ||
		math.Hypot(e.planOrigin.x-fromX, e.planOrigin.y-fromY) > 8 ||
		math.Hypot(e.planAvoid.x-avoidX, e.planAvoid.y-avoidY) > 3
	if !needsPlan {
		e.route = terrain.AdvanceTargetRoute(e.route, fromX, fromY, avoidX, avoidY, 5)
		if len(e.route) == 0 || !terrain.HasClearFollowSegmentAvoid(fromX, fromY,
			e.route[0].X, e.route[0].Y, avoidX, avoidY, 5) {
			needsPlan = true
		}
	}
	if needsPlan {
		waypoints, ok := terrain.TargetWaypointsAvoid(fromX, fromY,
			targetX, targetY, avoidX, avoidY, 5)
		if !ok || len(waypoints) == 0 ||
			!terrain.HasClearFollowSegmentAvoid(fromX, fromY,
				waypoints[0].X, waypoints[0].Y, avoidX, avoidY, 5) {
			e.route = nil
			e.routeTerrain = nil
			return 0, 0, false
		}
		e.route = waypoints
		e.routeTerrain = terrain
		e.planOrigin = point{fromX, fromY}
		e.planTarget = point{targetX, targetY}
		e.planAvoid = point{avoidX, avoidY}
	}
	dx, dy := e.route[0].X-fromX, e.route[0].Y-fromY
	length := math.Hypot(dx, dy)
	if length == 0 {
		return 0, 0, false
	}
	return dx / length, dy / length, true
}
