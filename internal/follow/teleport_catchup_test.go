package follow

import (
	"testing"
	"time"
)

func TestTargetTrailLeaderJumpStartsAndReleasesTeleportCatchUp(t *testing.T) {
	for _, scenario := range []struct {
		name       string
		normal     bool
		jumpLeader float64
		releaseAt  float64
		released   State
	}{
		{name: "normal", normal: true, jumpLeader: 70, releaseAt: 15, released: Following},
		{name: "classic", jumpLeader: 50, releaseAt: 42, released: Holding},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			engine, ok := New(Config{StopDistance: 9, ResumeDistance: 12,
				CatchUpDistance: 90, MaxAge: 5 * time.Second, TargetTrail: true,
				NormalSprintCatchUp: scenario.normal})
			if !ok {
				t.Fatal("invalid follow config")
			}
			position := func(x float64, at time.Time) Position {
				return Position{X: x, Area: "instance", Seen: at, Valid: true}
			}
			at := time.Unix(300, 0)
			if got := engine.Step(at, position(0, at), position(0, at)); got.State != Holding {
				t.Fatalf("initial stop was not held: %+v", got)
			}
			at = at.Add(time.Second)
			if got := engine.Step(at, position(scenario.jumpLeader, at), position(0, at)); got.State != CatchingUp || got.Reason != "Teleport catch-up" || !engine.teleportRecovery {
				t.Fatalf("leader jump below normal entry did not enter teleport recovery: %+v", got)
			}
			at = at.Add(time.Second)
			if got := engine.Step(at, position(scenario.jumpLeader, at), position(scenario.releaseAt, at)); got.State != scenario.released || got.Reason != "" || engine.teleportRecovery {
				t.Fatalf("teleport recovery did not release at the mode threshold: %+v", got)
			}
		})
	}
}

func TestLeaderJumpRestartsNormalSprintDirectionDelay(t *testing.T) {
	engine, ok := New(Config{StopDistance: 9, ResumeDistance: 12,
		CatchUpDistance: 90, MaxAge: 5 * time.Second, TargetTrail: true,
		NormalSprintCatchUp: true})
	if !ok {
		t.Fatal("invalid follow config")
	}
	position := func(x float64, at time.Time) Position {
		return Position{X: x, Area: "instance", Seen: at, Valid: true}
	}
	at := time.Unix(400, 0)
	engine.Step(at, position(100, at), position(0, at))
	at = at.Add(250 * time.Millisecond)
	if got := engine.Step(at, position(100, at), position(0, at)); !got.SprintDown {
		t.Fatalf("pre-jump normal sprint did not engage: %+v", got)
	}
	at = at.Add(250 * time.Millisecond)
	if got := engine.Step(at, position(170, at), position(100, at)); got.State != CatchingUp || got.Reason != "Teleport catch-up" || got.SprintDown {
		t.Fatalf("leader jump kept a stale sprint direction: %+v", got)
	}
}
