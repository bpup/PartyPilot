package follow

import (
	"math"
	"testing"
	"time"

	"partypilot/internal/navigation"
)

func TestFollowHysteresisAndCatchUp(t *testing.T) {
	now := time.Unix(10, 0)
	engine, ok := New(Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second})
	if !ok {
		t.Fatal("invalid test config")
	}
	leader := Position{X: 4, Area: "instance-1", Seen: now, Valid: true}
	follower := Position{Area: "instance-1", Seen: now, Valid: true}
	got := engine.Step(now, leader, follower)
	if got.State != Following || got.X != 1 || got.Y != 0 {
		t.Fatalf("follow: %+v", got)
	}
	leader.X = 2.5
	if got = engine.Step(now, leader, follower); got.State != Following {
		t.Fatalf("hysteresis: %+v", got)
	}
	leader.X = 2
	if got = engine.Step(now, leader, follower); got.State != Holding {
		t.Fatalf("stop: %+v", got)
	}
	leader.X = 2.5
	if got = engine.Step(now, leader, follower); got.State != Holding {
		t.Fatalf("resume threshold: %+v", got)
	}
	leader.X = 11
	if got = engine.Step(now, leader, follower); got.State != CatchingUp {
		t.Fatalf("catch up: %+v", got)
	}
	leader.X = 8
	if got = engine.Step(now, leader, follower); got.State != CatchingUp {
		t.Fatalf("catch up released above target threshold: %+v", got)
	}
	leader.X = 6.5
	if got = engine.Step(now, leader, follower); got.State != CatchingUp {
		t.Fatalf("classic catch up released before stop distance: %+v", got)
	}
	leader.X = 2
	if got = engine.Step(now, leader, follower); got.State != Holding {
		t.Fatalf("classic catch up did not end at stop distance: %+v", got)
	}
	leader.X = 9
	if got = engine.Step(now, leader, follower); got.State != Following {
		t.Fatalf("catch up restarted below entry threshold: %+v", got)
	}
	leader.X = 11
	if got = engine.Step(now, leader, follower); got.State != CatchingUp {
		t.Fatalf("catch up did not restart at entry threshold: %+v", got)
	}
	engine.Reset()
	leader.X = 8
	if got = engine.Step(now, leader, follower); got.State != Following {
		t.Fatalf("reset retained catch up state: %+v", got)
	}
}

func TestTerrainRouteTurnsAndMissingMapStops(t *testing.T) {
	now := time.Unix(50, 0)
	engine, ok := New(Config{StopDistance: 1, ResumeDistance: 2, CatchUpDistance: 20,
		MaxAge: time.Second, RequireTerrain: true})
	if !ok {
		t.Fatal("invalid terrain config")
	}
	leader := Position{X: 4.5, Y: 2.5, Area: "instance", Seen: now, Valid: true}
	follower := Position{X: 0.5, Y: 2.5, Area: "instance", Seen: now, Valid: true}
	grid := &navigation.Grid{Area: "instance", CellSize: 1, Width: 5, Height: 5,
		Blocked: []navigation.Cell{{X: 2, Y: 1}, {X: 2, Y: 2}, {X: 2, Y: 3}}}
	if got := engine.StepWithTerrain(now, leader, follower, grid); got.State != Following || got.X != 1 || got.Y != 0 {
		t.Fatalf("first route cell: %+v", got)
	}
	follower.X = 1.5
	if got := engine.StepWithTerrain(now, leader, follower, grid); got.State != Following || got.X != 0 || got.Y != -1 {
		t.Fatalf("route failed to turn around obstacle: %+v", got)
	}
	if got := engine.StepWithTerrain(now, leader, follower, nil); got.State != Waiting {
		t.Fatalf("missing terrain produced motion: %+v", got)
	}
	grid.Area = "other-instance"
	if got := engine.StepWithTerrain(now, leader, follower, grid); got.State != Waiting {
		t.Fatalf("other-instance terrain produced motion: %+v", got)
	}
	grid.Area = "instance"
	grid.Blocked = append(grid.Blocked, navigation.Cell{X: 2, Y: 0}, navigation.Cell{X: 2, Y: 4})
	if got := engine.StepWithTerrain(now, leader, follower, grid); got.State != Waiting {
		t.Fatalf("unreachable terrain produced motion: %+v", got)
	}
	optional, _ := New(Config{StopDistance: 1, ResumeDistance: 2, CatchUpDistance: 20, MaxAge: time.Second})
	known := &navigation.Grid{Area: "instance", CellSize: 1, Width: 5, Height: 5}
	if got := optional.StepWithTerrain(now, leader, follower, known); got.State != Following {
		t.Fatalf("optional terrain did not route: %+v", got)
	}
	if got := optional.StepWithTerrain(now, leader, follower, nil); got.State != Waiting {
		t.Fatalf("missing terrain silently fell back to direct movement: %+v", got)
	}
}

func TestClassicCatchUpEntryStaysBeyondStopDistance(t *testing.T) {
	now := time.Unix(49, 0)
	engine, ok := New(Config{StopDistance: 2, ResumeDistance: 2.2, CatchUpDistance: 2.5,
		MaxAge: time.Second})
	if !ok {
		t.Fatal("invalid test config")
	}
	leader := Position{X: 2.5, Area: "instance", Seen: now, Valid: true}
	follower := Position{Area: "instance", Seen: now, Valid: true}
	if got := engine.Step(now, leader, follower); got.State != Following {
		t.Fatalf("catch up began within one unit of stop distance: %+v", got)
	}
	leader.X = 3
	if got := engine.Step(now, leader, follower); got.State != CatchingUp {
		t.Fatalf("catch up did not begin at the classic entry distance: %+v", got)
	}
}

func TestTargetTrailUsesTrailingPointAroundTurn(t *testing.T) {
	now := time.Unix(52, 0)
	engine, ok := New(Config{StopDistance: 8, ResumeDistance: 9, CatchUpDistance: 30,
		MaxAge: time.Second, TargetTrail: true})
	if !ok {
		t.Fatal("target trail configuration rejected")
	}
	position := func(x, y float64, seen time.Time) Position {
		return Position{X: x, Y: y, Area: "instance", Seen: seen, Valid: true}
	}
	engine.Step(now, position(0, 0, now), position(-20, 0, now))
	engine.Step(now.Add(time.Millisecond), position(10, 0, now.Add(time.Millisecond)),
		position(-20, 0, now.Add(time.Millisecond)))
	at := now.Add(2 * time.Millisecond)
	command := engine.Step(at, position(10, 10, at), position(12, -2, at))
	if command.State != CatchingUp || command.X > -0.4 || command.Y < 0.8 {
		t.Fatalf("target trail did not aim at the trailing bend: %+v", command)
	}
	if len(engine.trail) != 3 {
		t.Fatalf("target trail did not retain sampled bend: %+v", engine.trail)
	}
	jump := now.Add(3 * time.Millisecond)
	engine.Step(jump, position(100, 100, jump), position(50, 50, jump))
	if len(engine.trail) != 1 {
		t.Fatalf("target trail did not reset after a large leader jump: %+v", engine.trail)
	}
	velocityEngine, _ := New(Config{StopDistance: 8, ResumeDistance: 9,
		CatchUpDistance: 30, MaxAge: time.Second, TargetTrail: true})
	velocityEngine.Step(now, position(0, 0, now), position(-20, 0, now))
	slow := now.Add(time.Second)
	velocityEngine.Step(slow, position(2, 0, slow), position(-20, 0, slow))
	fast := slow.Add(time.Millisecond)
	velocityEngine.Step(fast, position(14, 0, fast), position(-20, 0, fast))
	if len(velocityEngine.trail) != 1 {
		t.Fatalf("target trail did not reset after a sudden speed jump: %+v", velocityEngine.trail)
	}
}

func TestTargetTrailHoldsNearWaypoint(t *testing.T) {
	now := time.Unix(53, 0)
	engine, ok := New(Config{StopDistance: 0, ResumeDistance: 1,
		CatchUpDistance: 20, MaxAge: time.Second, TargetTrail: true})
	if !ok {
		t.Fatal("target trail configuration rejected")
	}
	leader := Position{X: 10, Area: "instance", Seen: now, Valid: true}
	follower := Position{X: 8, Area: "instance", Seen: now, Valid: true}
	if got := engine.Step(now, leader, follower); got.State != Holding || got.X != 0 || got.Y != 0 {
		t.Fatalf("near trailing waypoint emitted an invalid movement direction: %+v", got)
	}
}

func TestTargetTrailHoldReleasesAfterTargetShift(t *testing.T) {
	now := time.Unix(55, 0)
	engine, ok := New(Config{StopDistance: 2, ResumeDistance: 3,
		CatchUpDistance: 30, MaxAge: 5 * time.Second, TargetTrail: true})
	if !ok {
		t.Fatal("target trail configuration rejected")
	}
	position := func(x float64, at time.Time) Position {
		return Position{X: x, Area: "instance", Seen: at, Valid: true}
	}
	engine.Step(now, position(10, now), position(8, now))
	at := now.Add(time.Second)
	if got := engine.Step(at, position(11, at), position(8, at)); got.State != Holding ||
		!engine.holdingTarget || math.Abs(engine.heldTarget.x-9) > 0.001 {
		t.Fatalf("near trail target did not latch its hold: %+v", got)
	}
	at = now.Add(2 * time.Second)
	if got := engine.Step(at, position(11, at), position(20, at)); got.State != Holding {
		t.Fatalf("unchanged trail target released its hold: %+v", got)
	}
	at = now.Add(3 * time.Second)
	if got := engine.Step(at, position(14, at), position(20, at)); got.State != Following ||
		engine.holdingTarget || got.X >= 0 {
		t.Fatalf("shifted trail target did not release its hold: %+v", got)
	}
	at = now.Add(4 * time.Second)
	engine.Step(at, position(14, at), position(10, at))
	if !engine.holdingTarget {
		t.Fatal("test did not restore a trail-target hold")
	}
	at = now.Add(5 * time.Second)
	if got := engine.Step(at, position(100, at), position(10, at)); got.State == Holding ||
		engine.holdingTarget {
		t.Fatalf("leader jump retained a stale held target: %+v", got)
	}
}

func TestTargetTrailAvoidsLeaderOnDirectSegment(t *testing.T) {
	now := time.Unix(54, 0)
	engine, ok := New(Config{StopDistance: 8, ResumeDistance: 9,
		CatchUpDistance: 30, MaxAge: 2 * time.Second, TargetTrail: true})
	if !ok {
		t.Fatal("target trail configuration rejected")
	}
	position := func(x, y float64, at time.Time) Position {
		return Position{X: x, Y: y, Area: "instance", Seen: at, Valid: true}
	}
	engine.Step(now, position(0, 0, now), position(-20, 0, now))
	at := now.Add(time.Second)
	leader := position(10, 0, at)
	clear := engine.Step(at, leader, position(-20, 0, at))
	if clear.Reason != "" || clear.X < 0.99 || math.Abs(clear.Y) > 0.01 {
		t.Fatalf("clear trail segment was diverted: %+v", clear)
	}
	near := engine.Step(at, leader, position(15, 10, at))
	if near.Reason != "Player avoidance" || near.State != CatchingUp ||
		near.X > -0.85 || near.Y > -0.35 {
		t.Fatalf("segment near leader did not use target detour: %+v", near)
	}
	if got := segmentDistance(10, 0, 15, 10, 2, 0); got >= 5 {
		t.Fatalf("test segment is not near the leader: %v", got)
	}
}

func TestTrailAndTerrainAdvanceBeforeNextTrailSample(t *testing.T) {
	now := time.Unix(51, 0)
	engine, ok := New(Config{StopDistance: 1, ResumeDistance: 2, CatchUpDistance: 10,
		MaxAge: time.Second, TrailSpacing: 5, TrailMaxStep: 6, RequireTerrain: true})
	if !ok {
		t.Fatal("invalid combined config")
	}
	grid := &navigation.Grid{Area: "instance", CellSize: 1, Width: 5, Height: 1}
	leader := Position{X: 0.5, Y: 0.5, Area: "instance", Seen: now, Valid: true}
	follower := leader
	engine.StepWithTerrain(now, leader, follower, grid)
	leader.X = 4.5
	if got := engine.StepWithTerrain(now, leader, follower, grid); got.State != Following || got.X <= 0 {
		t.Fatalf("unsampled trail waypoint blocked movement: %+v", got)
	}
}

func TestUnsafeObservationsStopMovement(t *testing.T) {
	now := time.Unix(10, 0)
	for name, mutate := range map[string]func(*Position, *Position){
		"other instance": func(l, f *Position) { f.Area = "instance-2" },
		"stale":          func(l, f *Position) { l.Seen = now.Add(-2 * time.Second) },
		"future":         func(l, f *Position) { f.Seen = now.Add(time.Second) },
		"missing":        func(l, f *Position) { l.Valid = false },
		"nan":            func(l, f *Position) { l.X = math.NaN() },
	} {
		t.Run(name, func(t *testing.T) {
			engine, _ := New(Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second})
			leader := Position{X: 20, Area: "instance-1", Seen: now, Valid: true}
			follower := Position{Area: "instance-1", Seen: now, Valid: true}
			if got := engine.Step(now, leader, follower); got.State != CatchingUp {
				t.Fatalf("initial: %+v", got)
			}
			mutate(&leader, &follower)
			if got := engine.Step(now, leader, follower); got.State != Waiting {
				t.Fatalf("unsafe: %+v", got)
			}
		})
	}
}

func TestInvalidConfiguration(t *testing.T) {
	if _, ok := New(Config{StopDistance: 3, ResumeDistance: 2, CatchUpDistance: 10, MaxAge: time.Second}); ok {
		t.Fatal("accepted reversed distances")
	}
	if _, ok := New(Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second, TrailSpacing: 1}); ok {
		t.Fatal("accepted trail without a jump limit")
	}
	if _, ok := New(Config{StopDistance: math.NaN(), ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}); ok {
		t.Fatal("accepted NaN stop distance")
	}
}

func TestTrailTurnsBeforeDirectLineAndStopsOnJump(t *testing.T) {
	now := time.Unix(20, 0)
	engine, ok := New(Config{StopDistance: 1, ResumeDistance: 2, CatchUpDistance: 30,
		MaxAge: time.Second, TrailSpacing: 1, TrailMaxStep: 5})
	if !ok {
		t.Fatal("invalid trail config")
	}
	leader := Position{Area: "instance-1", Seen: now, Valid: true}
	follower := Position{X: -10, Area: "instance-1", Seen: now, Valid: true}
	if got := engine.Step(now, leader, follower); got.X != 1 || got.Y != 0 {
		t.Fatalf("initial route: %+v", got)
	}
	leader.Y = 4
	if got := engine.Step(now, leader, follower); got.X != 1 || got.Y != 0 {
		t.Fatalf("trail cut the corner: %+v", got)
	}
	follower.X = 0
	if got := engine.Step(now, leader, follower); got.X != 0 || got.Y != 1 {
		t.Fatalf("trail did not advance after waypoint: %+v", got)
	}
	leader.Y = 20
	if got := engine.Step(now, leader, follower); got.State != Waiting {
		t.Fatalf("large leader jump did not stop: %+v", got)
	}
	if got := engine.Step(now, leader, follower); got.State != Waiting {
		t.Fatalf("large leader jump resumed without reset: %+v", got)
	}
	leader.Seen = now.Add(-2 * time.Second)
	if got := engine.Step(now, leader, follower); got.State != Waiting {
		t.Fatalf("stale observation did not wait: %+v", got)
	}
	leader.Seen = now
	if got := engine.Step(now, leader, follower); got.State != Waiting {
		t.Fatalf("fresh observation cleared jump lock: %+v", got)
	}
	engine.Reset()
	if got := engine.Step(now, leader, follower); got.State != Following {
		t.Fatalf("explicit reset did not allow a fresh route: %+v", got)
	}
}
