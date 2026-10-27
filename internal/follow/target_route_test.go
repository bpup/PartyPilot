package follow

import (
	"math"
	"testing"

	"partypilot/internal/navigation"
)

func TestTargetRouteReusesReplansAndClearsOnDirectSight(t *testing.T) {
	engine, ok := New(Config{StopDistance: 1, ResumeDistance: 2,
		CatchUpDistance: 30, MaxAge: 1})
	if !ok {
		t.Fatal("invalid follow config")
	}
	grid := &navigation.Grid{Area: "instance", CellSize: 1, Width: 50,
		Height: 40, TargetNavigation: true}
	for y := 5; y <= 15; y++ {
		grid.Blocked = append(grid.Blocked, navigation.Cell{X: 15, Y: y})
	}
	if _, _, ok := engine.targetRouteDirection(grid, 5, 10, 30, 10, 40, 30); !ok {
		t.Fatal("initial route missing")
	}
	if _, _, ok := engine.targetRouteDirection(grid, 7, 10, 30, 10, 40, 30); !ok ||
		engine.planOrigin != (point{5, 10}) {
		t.Fatal("usable route was not reused")
	}
	waypoint := engine.route[0]
	grid.Blocked = append(grid.Blocked, navigation.Cell{
		X: int(math.Round(waypoint.X)), Y: int(math.Round(waypoint.Y))})
	if _, dy, ok := engine.targetRouteDirection(grid, 7, 10, 30, 10, 40, 30); !ok ||
		dy == 0 || engine.planOrigin != (point{7, 10}) {
		t.Fatal("blocked cached segment was not replanned around")
	}
	if dx, dy, ok := engine.targetRouteDirection(grid, 20, 10, 30, 10, 40, 30); !ok ||
		dx < 0.99 || math.Abs(dy) > 0.001 || len(engine.route) != 0 || engine.routeTerrain != nil {
		t.Fatal("clear direct segment retained a stale detour")
	}
	engine.Reset()
	if len(engine.route) != 0 || engine.routeTerrain != nil {
		t.Fatal("reset retained an old terrain route")
	}
}
