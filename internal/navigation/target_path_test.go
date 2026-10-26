package navigation

import (
	"math"
	"testing"
)

func TestTargetNavigationUsesCenteredCoordinatesAndDirectSight(t *testing.T) {
	g := &Grid{Area: "instance", CellSize: 1, Width: 12, Height: 12,
		TargetNavigation: true}
	points, ok := g.TargetWaypoints(2, 2, 8, 8)
	if !ok || len(points) != 1 || points[0] != (Waypoint{8, 8}) {
		t.Fatalf("clear diagonal was not simplified: %v, %v", points, ok)
	}
	dx, dy, ok := g.NextDirection(2, 2, 8, 8)
	if !ok || math.Abs(dx-math.Sqrt2/2) > 1e-9 || math.Abs(dy-math.Sqrt2/2) > 1e-9 {
		t.Fatalf("centered diagonal direction changed: %f, %f, %v", dx, dy, ok)
	}
}

func TestTargetNavigationRejectsDiagonalCornerAndNarrowChannel(t *testing.T) {
	g := &Grid{Area: "instance", CellSize: 1, Width: 9, Height: 9,
		TargetNavigation: true}
	for y := 1; y <= 7; y++ {
		g.Blocked = append(g.Blocked, Cell{X: 4, Y: y})
	}
	if points, ok := g.TargetWaypoints(2, 4, 6, 4); ok {
		t.Fatalf("one-cell border gap bypassed clearance: %v", points)
	}
	if _, _, ok := g.NextDirection(2, 4, 6, 4); ok {
		t.Fatal("unreachable target produced movement")
	}
	if _, ok := g.TargetWaypoints(math.MaxFloat64, 4, 6, 4); ok ||
		g.HasLineOfSight(math.MaxFloat64, 4, 6, 4, 0) {
		t.Fatal("extreme finite coordinates bypassed bounded grid checks")
	}
}

func TestTargetNavigationAvoidsObservedLeaderAcrossEntireRoute(t *testing.T) {
	g := &Grid{Area: "instance", CellSize: 1, Width: 40, Height: 31,
		TargetNavigation: true}
	if direct, ok := g.TargetWaypoints(5, 15, 25, 15); !ok ||
		len(direct) != 1 || direct[0] != (Waypoint{25, 15}) {
		t.Fatalf("open map did not use direct route: %v, %v", direct, ok)
	}
	waypoints, ok := g.TargetWaypointsAvoid(5, 15, 25, 15, 15, 15, 5)
	if !ok || len(waypoints) == 0 || waypoints[0].Y == 15 {
		t.Fatalf("leader avoidance did not bend route: %v, %v", waypoints, ok)
	}
	access := newGridAccess(g)
	previous := Waypoint{5, 15}
	avoid := &avoidRegion{x: 15, y: 15, radius: 5}
	for _, waypoint := range waypoints {
		if !g.lineOfSight(access, previous.X, previous.Y,
			waypoint.X, waypoint.Y, 0, avoid) {
			t.Fatalf("simplified route crossed leader region: %v", waypoints)
		}
		previous = waypoint
	}
	if _, ok := g.TargetWaypointsAvoid(5, 15, 25, 15, 15, 15, math.NaN()); ok {
		t.Fatal("invalid avoidance radius was accepted")
	}
	if _, ok := g.TargetWaypointsAvoid(5, 15, 25, 15, 15, 15, 100); ok {
		t.Fatal("unavoidable leader exclusion produced a route")
	}
	g.OriginX, g.CellSize = math.MaxFloat64, math.MaxFloat64
	if g.Valid() {
		t.Fatal("overflowing grid coordinate mapping was accepted")
	}
}
