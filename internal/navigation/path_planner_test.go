package navigation

import (
	"context"
	"sync/atomic"
	"testing"
	"time"
)

func awaitPlan(t *testing.T, planner *PathPlanner, grid *Grid,
	from, goal, avoid Waypoint, want PathState) PathPlan {
	t.Helper()
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		plan := planner.Request(grid, from, goal, avoid, 0)
		if plan.State == want {
			return plan
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatalf("planner never returned %s", want)
	return PathPlan{}
}

func TestPathPlannerPendingCacheAndExpiry(t *testing.T) {
	grid := &Grid{Area: "instance", CellSize: 1, Width: 40, Height: 30,
		TargetNavigation: true}
	from, goal, avoid := Waypoint{5, 10}, Waypoint{25, 10}, Waypoint{35, 20}
	started := make(chan struct{})
	release := make(chan struct{})
	var calls atomic.Int32
	planner := newPathPlanner(func(ctx context.Context, _ *Grid,
		_, goal, _ Waypoint) []Waypoint {
		calls.Add(1)
		close(started)
		select {
		case <-release:
		case <-ctx.Done():
			return nil
		}
		return []Waypoint{goal}
	}, time.Now)
	defer planner.Close()
	if got := planner.Request(grid, from, goal, avoid, 0); got.State != PathPending {
		t.Fatalf("new search did not return pending: %+v", got)
	}
	<-started
	if got := planner.Request(grid, from, goal, avoid, 0); got.State != PathPending {
		t.Fatalf("running search was not pending: %+v", got)
	}
	close(release)
	ready := awaitPlan(t, planner, grid, from, goal, avoid, PathReady)
	if len(ready.Route) != 1 || ready.Route[0] != goal || calls.Load() != 1 {
		t.Fatalf("ready plan was not cached: %+v, searches=%d", ready, calls.Load())
	}
	ready.Route[0] = Waypoint{0, 0}
	if again := planner.Request(grid, from, goal, avoid, 0); again.Route[0] != goal {
		t.Fatal("caller mutated cached route")
	}
}

func TestPathPlannerClearDiscardsStaleResult(t *testing.T) {
	grid := &Grid{Area: "instance", CellSize: 1, Width: 40, Height: 30,
		TargetNavigation: true}
	from, goal, avoid := Waypoint{5, 10}, Waypoint{25, 10}, Waypoint{35, 20}
	started := make(chan struct{})
	release := make(chan struct{})
	planner := newPathPlanner(func(ctx context.Context, _ *Grid,
		_, goal, _ Waypoint) []Waypoint {
		close(started)
		<-release
		return []Waypoint{goal}
	}, time.Now)
	if planner.Request(grid, from, goal, avoid, 0).State != PathPending {
		t.Fatal("new search should be pending")
	}
	<-started
	planner.Clear()
	planner.Close()
	close(release)
	if got := planner.Request(grid, from, goal, avoid, 0); got.State != PathUnreachable {
		t.Fatalf("closed planner accepted a stale route: %+v", got)
	}
}

func TestPathPlannerQueuesByPriorityWithoutUsingOldGoal(t *testing.T) {
	grid := &Grid{Area: "instance", CellSize: 1, Width: 50, Height: 40,
		TargetNavigation: true}
	from, avoid := Waypoint{5, 10}, Waypoint{40, 30}
	started := make(chan float64, 3)
	release := make(chan struct{})
	nextStarted := func() float64 {
		select {
		case value := <-started:
			return value
		case <-time.After(2 * time.Second):
			t.Fatal("queued planner search did not start")
			return 0
		}
	}
	planner := newPathPlanner(func(_ context.Context, _ *Grid,
		_, goal, _ Waypoint) []Waypoint {
		started <- goal.X
		if goal.X == 20 {
			<-release
		}
		return []Waypoint{goal}
	}, time.Now)
	defer planner.Close()
	if planner.Request(grid, from, Waypoint{20, 10}, avoid, 0).State != PathPending {
		t.Fatal("first route not queued")
	}
	if got := nextStarted(); got != 20 {
		t.Fatalf("unexpected first route: %v", got)
	}
	planner.Request(grid, from, Waypoint{22, 10}, avoid, 5)
	planner.Request(grid, from, Waypoint{24, 10}, avoid, 0)
	if got := planner.Request(grid, from, Waypoint{24, 10}, avoid, 0); got.State != PathPending {
		t.Fatalf("new target reused previous result: %+v", got)
	}
	close(release)
	if got := nextStarted(); got != 24 {
		t.Fatalf("higher priority route was not next: %v", got)
	}
	if got := nextStarted(); got != 22 {
		t.Fatalf("lower priority route was not last: %v", got)
	}
	ready := awaitPlan(t, planner, grid, from, Waypoint{24, 10}, avoid, PathReady)
	if len(ready.Route) != 1 || ready.Route[0].X != 24 {
		t.Fatalf("old goal was returned for new request: %+v", ready)
	}
}

func TestPathPlannerReadyAndFailedCacheLifetimes(t *testing.T) {
	grid := &Grid{Area: "instance", CellSize: 1, Width: 50, Height: 40,
		TargetNavigation: true}
	from, goal, avoid := Waypoint{5, 10}, Waypoint{25, 10}, Waypoint{40, 30}
	var ticks atomic.Int64
	ticks.Store(time.Unix(100, 0).UnixNano())
	clock := func() time.Time { return time.Unix(0, ticks.Load()) }
	var calls atomic.Int32
	planner := newPathPlanner(func(_ context.Context, _ *Grid,
		_, goal, _ Waypoint) []Waypoint {
		calls.Add(1)
		return []Waypoint{goal}
	}, clock)
	defer planner.Close()
	if planner.Request(grid, from, goal, avoid, 0).State != PathPending {
		t.Fatal("new ready route was not pending")
	}
	awaitPlan(t, planner, grid, from, goal, avoid, PathReady)
	ticks.Add(int64(2 * time.Second))
	if planner.Request(grid, from, goal, avoid, 0).State != PathPending {
		t.Fatal("ready route survived the one-second cache lifetime")
	}
	awaitPlan(t, planner, grid, from, goal, avoid, PathReady)
	if calls.Load() != 2 {
		t.Fatalf("ready cache did not replan: %d searches", calls.Load())
	}
	failed := newPathPlanner(func(context.Context, *Grid,
		Waypoint, Waypoint, Waypoint) []Waypoint {
		return nil
	}, clock)
	defer failed.Close()
	if failed.Request(grid, from, goal, avoid, 0).State != PathPending {
		t.Fatal("new failed route was not pending")
	}
	awaitPlan(t, failed, grid, from, goal, avoid, PathUnreachable)
	ticks.Add(int64(4 * time.Second))
	if failed.Request(grid, from, goal, avoid, 0).State != PathUnreachable {
		t.Fatal("failed route expired before five seconds")
	}
	ticks.Add(int64(2 * time.Second))
	if failed.Request(grid, from, goal, avoid, 0).State != PathPending {
		t.Fatal("failed route survived beyond five seconds")
	}
}
