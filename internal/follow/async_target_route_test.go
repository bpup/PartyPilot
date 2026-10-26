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
	leader := Position{X: 25, Y: 15, Area: "instance", Seen: at, Valid: true}
	follower := Position{X: 5, Y: 15, Area: "instance", Seen: at, Valid: true}
	first := engine.StepWithTerrain(at, leader, follower, grid)
	if first.State != Following || first.Reason != "Planning route" ||
		first.X != 0 || first.Y != 0 || first.SprintDown {
		t.Fatalf("async route moved before search completion: %+v", first)
	}
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		command := engine.StepWithTerrain(at.Add(time.Millisecond), leader, follower, grid)
		if command.Reason != "Planning route" {
			if command.State != Following || command.Y == 0 {
				t.Fatalf("ready detour missing: %+v", command)
			}
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatal("async route never completed")
}
