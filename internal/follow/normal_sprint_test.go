package follow

import (
	"testing"
	"time"

	"partypilot/internal/navigation"
)

func TestNormalSprintNeedsStableDirectDirectionAndReleasesBeforeStop(t *testing.T) {
	at := time.Unix(200, 0)
	engine, ok := New(Config{StopDistance: 9, ResumeDistance: 12,
		CatchUpDistance: 20, MaxAge: time.Second, TargetTrail: true,
		NormalSprintCatchUp: true})
	if !ok {
		t.Fatal("invalid normal sprint config")
	}
	position := func(x float64, seen time.Time) Position {
		return Position{X: x, Area: "instance", Seen: seen, Valid: true}
	}
	if got := engine.Step(at, position(40, at), position(0, at)); got.State != CatchingUp || got.SprintDown {
		t.Fatalf("normal sprint pressed before direction stabilized: %+v", got)
	}
	at = at.Add(250 * time.Millisecond)
	if got := engine.Step(at, position(40, at), position(0, at)); got.State != CatchingUp || !got.SprintDown {
		t.Fatalf("stable direct direction did not start normal sprint: %+v", got)
	}
	at = at.Add(100 * time.Millisecond)
	if got := engine.Step(at, position(40, at), position(28, at)); got.State != Following || got.SprintDown {
		t.Fatalf("normal catch-up did not release at 65 percent before stop distance: %+v", got)
	}
}

func TestNormalSprintDoesNotPressOnTerrainDetour(t *testing.T) {
	at := time.Unix(202, 0)
	grid := &navigation.Grid{Area: "instance", CellSize: 1, Width: 60, Height: 40,
		TargetNavigation: true}
	for y := 10; y <= 20; y++ {
		grid.Blocked = append(grid.Blocked, navigation.Cell{X: 16, Y: y})
	}
	engine, _ := New(Config{StopDistance: 9, ResumeDistance: 12,
		CatchUpDistance: 20, MaxAge: time.Second, TargetTrail: true,
		NormalSprintCatchUp: true})
	leader := Position{X: 45.5, Y: 15.5, Area: "instance", Seen: at, Valid: true}
	follower := Position{X: 5.5, Y: 15.5, Area: "instance", Seen: at, Valid: true}
	if got := engine.StepWithTerrain(at, leader, follower, grid); got.State != CatchingUp ||
		got.SprintDown || len(engine.route) == 0 {
		t.Fatalf("normal sprint moved before a valid terrain route: %+v", got)
	}
	leader.Seen, follower.Seen = at.Add(250*time.Millisecond), at.Add(250*time.Millisecond)
	if got := engine.StepWithTerrain(leader.Seen, leader, follower, grid); got.State != CatchingUp ||
		got.SprintDown || !engine.normalSprintSince.IsZero() {
		t.Fatalf("normal sprint pressed on a detour: %+v", got)
	}
}

func TestNormalSprintReleasesWhenTrailingTargetGetsClose(t *testing.T) {
	at := time.Unix(201, 0)
	engine, _ := New(Config{StopDistance: 9, ResumeDistance: 12,
		CatchUpDistance: 20, MaxAge: time.Second, TargetTrail: true,
		NormalSprintCatchUp: true})
	position := func(x, y float64, seen time.Time) Position {
		return Position{X: x, Y: y, Area: "instance", Seen: seen, Valid: true}
	}
	engine.Step(at, position(40, 0, at), position(0, 0, at))
	at = at.Add(250 * time.Millisecond)
	if !engine.Step(at, position(40, 0, at), position(0, 0, at)).SprintDown {
		t.Fatal("normal sprint did not reach stable direct state")
	}
	at = at.Add(100 * time.Millisecond)
	if got := engine.Step(at, position(40, 0, at), position(25, 0, at)); got.SprintDown ||
		!engine.normalSprintSince.IsZero() {
		t.Fatalf("short remaining trail kept normal sprint armed: %+v", got)
	}
}
