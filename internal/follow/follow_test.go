package follow

import (
	"math"
	"testing"
	"time"
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
