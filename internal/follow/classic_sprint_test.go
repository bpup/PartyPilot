package follow

import (
	"testing"
	"time"

	"partypilot/internal/navigation"
)

func TestClassicSprintRetriesAndFallsBackAfterThreeAttempts(t *testing.T) {
	at := time.Unix(100, 0)
	var sprint classicSprint
	follower := point{}
	check := func(offset time.Duration, expected bool, phase sprintPhase) {
		t.Helper()
		got := sprint.Update(true, true, false, follower, at.Add(offset))
		if got != expected || sprint.phase != phase {
			t.Fatalf("at %v: button=%v phase=%v attempt=%d", offset, got, sprint.phase, sprint.attempt)
		}
	}
	check(0, true, sprintHolding)
	check(800*time.Millisecond, true, sprintHolding)
	check(1200*time.Millisecond, false, sprintReleaseGap)
	check(1300*time.Millisecond, true, sprintHolding)
	check(2500*time.Millisecond, false, sprintReleaseGap)
	check(2600*time.Millisecond, true, sprintHolding)
	check(3400*time.Millisecond, false, sprintWalkingFallback)
	check(4*time.Second, false, sprintWalkingFallback)
	if sprint.Update(false, false, false, follower, at.Add(5*time.Second)) ||
		!sprint.Update(true, true, false, follower, at.Add(6*time.Second)) {
		t.Fatal("inactive sprint did not release and reset the fallback")
	}
}

func TestClassicSprintVerifiesObservedSpeed(t *testing.T) {
	at := time.Unix(101, 0)
	var sprint classicSprint
	if !sprint.Update(true, true, false, point{}, at) ||
		!sprint.Update(true, true, false, point{1, 0}, at.Add(20*time.Millisecond)) ||
		!sprint.Update(true, true, false, point{1, 0}, at.Add(800*time.Millisecond)) ||
		!sprint.verified {
		t.Fatal("measured sprint speed was not verified")
	}
}

func TestTargetTrailSprintButtonFollowsCatchUpAndHold(t *testing.T) {
	at := time.Unix(102, 0)
	engine, ok := New(Config{StopDistance: 2, ResumeDistance: 5,
		CatchUpDistance: 10, MaxAge: 5 * time.Second, TargetTrail: true})
	if !ok {
		t.Fatal("invalid target follow config")
	}
	position := func(x float64, seen time.Time) Position {
		return Position{X: x, Area: "instance", Seen: seen, Valid: true}
	}
	if got := engine.Step(at, position(40, at), position(0, at)); got.State != CatchingUp || !got.SprintDown {
		t.Fatalf("catch-up did not begin sprint engagement: %+v", got)
	}
	if got := engine.Step(at.Add(time.Second), position(40, at), position(0, at)); !got.SprintDown {
		t.Fatalf("same observation advanced the sprint timer: %+v", got)
	}
	later := at.Add(2 * time.Second)
	if got := engine.Step(later, position(40, later), position(10, later)); got.State != CatchingUp || got.SprintDown {
		t.Fatalf("missing speed did not start a retry release gap: %+v", got)
	}
	later = at.Add(3 * time.Second)
	if got := engine.Step(later, position(40, later), position(40, later)); got.State != Holding || got.SprintDown {
		t.Fatalf("holding retained the sprint button: %+v", got)
	}
}

func TestTargetTrailCollisionRequiresOptInAndMatchingTerrain(t *testing.T) {
	at := time.Unix(103, 0)
	grid := &navigation.Grid{Area: "instance", CellSize: 1, Width: 50, Height: 30,
		Blocked: []navigation.Cell{{X: 10, Y: 12}, {X: 10, Y: 15}, {X: 10, Y: 18}}}
	leader := Position{X: 40.5, Y: 15.5, Area: "instance", Seen: at, Valid: true}
	follower := Position{X: 2.5, Y: 15.5, Area: "instance", Seen: at, Valid: true}
	config := Config{StopDistance: 2, ResumeDistance: 5, CatchUpDistance: 10,
		MaxAge: 5 * time.Second, TargetTrail: true, ClassicCollisionDodge: true}
	engine, _ := New(config)
	if command := engine.StepWithTerrain(at, leader, follower, grid); command.State != CatchingUp {
		t.Fatalf("terrain route did not enter catch-up: %+v", command)
	}
	leader.Seen, follower.Seen = at.Add(100*time.Millisecond), at.Add(100*time.Millisecond)
	if command := engine.StepWithTerrain(leader.Seen, leader, follower, grid); command.State != CatchingUp ||
		engine.sprint.collisionSince.IsZero() {
		t.Fatalf("opted-in terrain collision was not observed: %+v", command)
	}
	config.ClassicCollisionDodge = false
	without, _ := New(config)
	without.StepWithTerrain(at, Position{X: 40.5, Y: 15.5, Area: "instance", Seen: at, Valid: true},
		Position{X: 2.5, Y: 15.5, Area: "instance", Seen: at, Valid: true}, grid)
	without.StepWithTerrain(leader.Seen, leader, follower, grid)
	if !without.sprint.collisionSince.IsZero() {
		t.Fatal("disabled collision option altered sprint state")
	}
}

func TestTargetTrailCollisionChecksRoutedWaypoint(t *testing.T) {
	at := time.Unix(104, 0)
	grid := &navigation.Grid{Area: "instance", CellSize: 1, Width: 60, Height: 40,
		TargetNavigation: true}
	for y := 10; y <= 20; y++ {
		grid.Blocked = append(grid.Blocked, navigation.Cell{X: 16, Y: y})
	}
	leader := Position{X: 45.5, Y: 15.5, Area: "instance", Seen: at, Valid: true}
	follower := Position{X: 5.5, Y: 15.5, Area: "instance", Seen: at, Valid: true}
	engine, ok := New(Config{StopDistance: 2, ResumeDistance: 5, CatchUpDistance: 10,
		MaxAge: time.Second, TargetTrail: true, ClassicCollisionDodge: true})
	if !ok {
		t.Fatal("invalid terrain follow config")
	}
	command := engine.StepWithTerrain(at, leader, follower, grid)
	if command.State != CatchingUp || len(engine.route) == 0 ||
		!grid.SprintCollisionImminent(follower.X, follower.Y, leader.X-2, leader.Y) ||
		grid.SprintCollisionImminent(follower.X, follower.Y,
			engine.route[0].X, engine.route[0].Y) {
		t.Fatalf("safe routed waypoint was treated as the blocked direct target: %+v route=%v collision=%v",
			command, engine.route, engine.sprint.collisionSince)
	}
	leader.Seen, follower.Seen = at.Add(100*time.Millisecond), at.Add(100*time.Millisecond)
	command = engine.StepWithTerrain(leader.Seen, leader, follower, grid)
	if command.State != CatchingUp || !engine.sprint.collisionSince.IsZero() {
		t.Fatalf("routed movement started a false collision retry: %+v route=%v collision=%v",
			command, engine.route, engine.sprint.collisionSince)
	}
}

func TestAsyncTargetTrailCollisionChecksRoutedWaypoint(t *testing.T) {
	at := time.Now()
	grid := &navigation.Grid{Area: "instance", CellSize: 1, Width: 60, Height: 40,
		TargetNavigation: true}
	for y := 10; y <= 20; y++ {
		grid.Blocked = append(grid.Blocked, navigation.Cell{X: 16, Y: y})
	}
	leader := Position{X: 45.5, Y: 15.5, Area: "instance", Seen: at, Valid: true}
	follower := Position{X: 5.5, Y: 15.5, Area: "instance", Seen: at, Valid: true}
	engine, ok := New(Config{StopDistance: 2, ResumeDistance: 5, CatchUpDistance: 10,
		MaxAge: time.Second, TargetTrail: true, AsyncTargetPath: true,
		ClassicCollisionDodge: true})
	if !ok {
		t.Fatal("invalid async terrain follow config")
	}
	defer engine.Close()
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		command := engine.StepWithTerrain(at, leader, follower, grid)
		if command.Reason != "Planning route" {
			if command.State != CatchingUp || len(engine.route) == 0 ||
				grid.SprintCollisionImminent(follower.X, follower.Y,
					engine.route[0].X, engine.route[0].Y) {
				t.Fatalf("async route did not produce a clear waypoint: %+v route=%v", command, engine.route)
			}
			leader.Seen, follower.Seen = at.Add(100*time.Millisecond), at.Add(100*time.Millisecond)
			command = engine.StepWithTerrain(leader.Seen, leader, follower, grid)
			if command.State != CatchingUp || !engine.sprint.collisionSince.IsZero() {
				t.Fatalf("async routed movement started a false collision retry: %+v route=%v",
					command, engine.route)
			}
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatal("async terrain route never completed")
}
