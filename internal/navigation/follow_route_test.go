package navigation

import "testing"

func TestFollowSegmentAllowsOnlyShortExitFromBlockedClearance(t *testing.T) {
	grid := &Grid{Area: "instance", CellSize: 1, Width: 25, Height: 25,
		TargetNavigation: true, Blocked: []Cell{{X: 4, Y: 5}}}
	if !grid.HasClearFollowSegmentAvoid(5, 5, 7, 5, 20, 20, 5) {
		t.Fatal("short clearance-zero exit from a blocked start was rejected")
	}
	if grid.HasClearFollowSegmentAvoid(5, 5, 10, 5, 20, 20, 5) {
		t.Fatal("long escape from a blocked start was allowed")
	}
	if grid.HasClearFollowSegmentAvoid(5, 5, 7, 5, 6, 5, 5) {
		t.Fatal("escape crossed the leader exclusion")
	}
}

func TestAdvanceTargetRouteSkipsReachedAndVisiblePoints(t *testing.T) {
	grid := &Grid{Area: "instance", CellSize: 1, Width: 30, Height: 30,
		TargetNavigation: true}
	route := []Waypoint{{8, 10}, {12, 10}, {16, 10}}
	advanced := grid.AdvanceTargetRoute(route, 8, 10, 25, 25, 5)
	if len(advanced) != 1 || advanced[0] != (Waypoint{16, 10}) {
		t.Fatalf("visible route points were not skipped: %v", advanced)
	}
	if len(route) != 3 {
		t.Fatal("advance mutated the cached source route")
	}
}

func TestFollowPathPrependsShortEscapeFromBlockedClearance(t *testing.T) {
	grid := &Grid{Area: "instance", CellSize: 1, Width: 25, Height: 25,
		TargetNavigation: true, Blocked: []Cell{{X: 4, Y: 5}}}
	route, ok := grid.TargetWaypointsAvoid(5, 5, 12, 5, 20, 20, 5)
	if !ok || len(route) < 2 || route[0] == (Waypoint{12, 5}) ||
		!grid.HasClearFollowSegmentAvoid(5, 5, route[0].X, route[0].Y, 20, 20, 5) {
		t.Fatalf("follow path did not prepend a safe short escape: %v, %v", route, ok)
	}
}
