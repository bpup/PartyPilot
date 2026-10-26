package follow

import (
	"testing"

	"partypilot/internal/navigation"
)

func TestTargetRouteReusesAndReplansAfterOriginDrift(t *testing.T) {
	engine, ok := New(Config{StopDistance: 1, ResumeDistance: 2,
		CatchUpDistance: 30, MaxAge: 1})
	if !ok {
		t.Fatal("invalid follow config")
	}
	grid := &navigation.Grid{Area: "instance", CellSize: 1, Width: 50,
		Height: 40, TargetNavigation: true}
	if _, _, ok := engine.targetRouteDirection(grid, 5, 10, 30, 10, 40, 30); !ok {
		t.Fatal("initial route missing")
	}
	if _, _, ok := engine.targetRouteDirection(grid, 7, 10, 30, 10, 40, 30); !ok ||
		engine.planOrigin != (point{5, 10}) {
		t.Fatal("usable route was not reused")
	}
	for y := 5; y <= 15; y++ {
		grid.Blocked = append(grid.Blocked, navigation.Cell{X: 15, Y: y})
	}
	if _, dy, ok := engine.targetRouteDirection(grid, 7, 10, 30, 10, 40, 30); !ok ||
		dy == 0 || engine.planOrigin != (point{7, 10}) {
		t.Fatal("blocked cached segment was not replanned around")
	}
	if _, _, ok := engine.targetRouteDirection(grid, 17, 10, 30, 10, 40, 30); !ok ||
		engine.planOrigin != (point{17, 10}) {
		t.Fatal("origin drift did not trigger replan")
	}
	engine.Reset()
	if len(engine.route) != 0 || engine.routeTerrain != nil {
		t.Fatal("reset retained an old terrain route")
	}
}
