package follow

import (
	"testing"
	"time"
)

func TestContinuousCloseFollowNeedsRecentLeaderMotion(t *testing.T) {
	engine, ok := New(Config{StopDistance: 10, ResumeDistance: 13,
		CatchUpDistance: 90, MaxAge: time.Second, TargetTrail: true})
	if !ok {
		t.Fatal("invalid follow configuration")
	}
	at := time.Unix(2100, 0).UTC()
	follower := Position{X: 0, Area: "instance", Seen: at, Valid: true}
	leader := Position{X: 9, Area: "instance", Seen: at, Valid: true}
	if got := engine.Step(at, leader, follower); got.State != Holding {
		t.Fatalf("fixture did not start inside the stop bubble: %+v", got)
	}
	if got := engine.StepWithTerrainCloseFollow(at, leader, follower, nil, true); got.State != Holding {
		t.Fatalf("stationary leader bypassed the stop bubble: %+v", got)
	}
	movedAt := at.Add(100 * time.Millisecond)
	leader.X, leader.Seen = 10, movedAt
	follower.Seen = movedAt
	if got := engine.Step(movedAt, leader, follower); got.State != Holding {
		t.Fatalf("ordinary follow did not hold before mirror continuation: %+v", got)
	}
	if got := engine.StepWithTerrainCloseFollow(movedAt, leader, follower, nil, true); got.State != Following || got.X <= 0 {
		t.Fatalf("same-frame continuation did not follow the moving leader: %+v", got)
	}
	staleAt := movedAt.Add(250 * time.Millisecond)
	if got := engine.StepWithTerrainCloseFollow(staleAt, leader, follower, nil, true); got.State != Holding {
		t.Fatalf("old leader motion kept continuous close follow: %+v", got)
	}
}
