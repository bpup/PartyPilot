package follow

import (
	"testing"
	"time"

	"partypilot/internal/navigation"
)

func TestAsyncTargetPathHoldsUntilRouteReady(t *testing.T) {
	engine, ok := New(Config{StopDistance: 9, ResumeDistance: 10,
		CatchUpDistance: 30, MaxAge: 5 * time.Second,
		TargetTrail: true, AsyncTargetPath: true})
	if !ok {
		t.Fatal("invalid async follow config")
	}
	defer engine.Close()
	grid := &navigation.Grid{Area: "instance", CellSize: 1,
		Width: 45, Height: 31, TargetNavigation: true}
	for y := 5; y <= 15; y++ {
		grid.Blocked = append(grid.Blocked, navigation.Cell{X: 10, Y: y})
	}
	at := time.Now()
	leader := Position{X: 35, Y: 15, Area: "instance", Seen: at, Valid: true}
	follower := Position{X: 5, Y: 15, Area: "instance", Seen: at, Valid: true}
	first := engine.StepWithTerrain(at, leader, follower, grid)
	if first.State != CatchingUp || first.Reason != "Planning route" ||
		first.X != 0 || first.Y != 0 || first.SprintDown {
		t.Fatalf("async route moved before search completion: %+v", first)
	}
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		command := engine.StepWithTerrain(at.Add(time.Millisecond), leader, follower, grid)
		if command.Reason != "Planning route" {
			if command.State != CatchingUp || command.Y == 0 {
				t.Fatalf("ready detour missing: %+v", command)
			}
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatal("async route never completed")
}

func TestAsyncTargetPathKeepsClearPreviousRouteDuringReplan(t *testing.T) {
	grid := &navigation.Grid{Area: "instance", CellSize: 1,
		Width: 50, Height: 40, TargetNavigation: true}
	for y := 5; y <= 25; y++ {
		grid.Blocked = append(grid.Blocked, navigation.Cell{X: 15, Y: y})
	}
	config := Config{StopDistance: 9, ResumeDistance: 10,
		CatchUpDistance: 30, MaxAge: 5 * time.Second,
		TargetTrail: true, AsyncTargetPath: true}
	engine, ok := New(config)
	if !ok {
		t.Fatal("invalid async follow config")
	}
	defer engine.Close()
	engine.route = []navigation.Waypoint{{X: 5, Y: 30}, {X: 35, Y: 30}}
	engine.routeTerrain = grid
	engine.planningTerrain = grid
	engine.planOrigin = point{5, 15}
	engine.planTarget = point{35, 10}
	engine.planAvoid = point{35, 10}
	dx, dy, state := engine.asyncTargetRouteDirection(grid, 5, 15, 35, 18, 35, 18)
	if state != navigation.PathReady || dx != 0 || dy <= 0 ||
		!engine.planPending || len(engine.route) == 0 {
		t.Fatalf("a valid prior waypoint stopped during a pending replan: %s %.2f %.2f", state, dx, dy)
	}

	blocked, _ := New(config)
	defer blocked.Close()
	blocked.route = []navigation.Waypoint{{X: 25, Y: 15}}
	blocked.routeTerrain = grid
	blocked.planningTerrain = grid
	blocked.planOrigin = point{5, 15}
	blocked.planTarget = point{35, 10}
	blocked.planAvoid = point{35, 10}
	dx, dy, state = blocked.asyncTargetRouteDirection(grid, 5, 15, 35, 18, 35, 18)
	if state != navigation.PathPending || dx != 0 || dy != 0 || len(blocked.route) != 0 {
		t.Fatalf("a blocked prior waypoint was reused: %s %.2f %.2f", state, dx, dy)
	}
}
