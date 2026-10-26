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
}
