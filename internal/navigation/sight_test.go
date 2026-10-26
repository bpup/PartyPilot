package navigation

import "testing"

func TestLineOfSightAndSprintCollision(t *testing.T) {
	g := &Grid{Area: "instance", CellSize: 1, Width: 30, Height: 30}
	for y := 0; y < 30; y++ {
		g.Blocked = append(g.Blocked, Cell{X: 10, Y: y})
	}
	if g.HasLineOfSight(2.5, 15.5, 25.5, 15.5, 0) ||
		!g.SprintCollisionImminent(2.5, 15.5, 25.5, 15.5) {
		t.Fatal("full wall failed to stop three forward rays")
	}
	if g.SprintCollisionImminent(2.5, 15.5, 8.5, 15.5) {
		t.Fatal("short segment reported imminent collision")
	}
	for index, cell := range g.Blocked {
		if cell.Y == 18 {
			g.Blocked = append(g.Blocked[:index], g.Blocked[index+1:]...)
			break
		}
	}
	if g.SprintCollisionImminent(2.5, 15.5, 25.5, 15.5) {
		t.Fatal("open parallel ray was ignored")
	}
	if !g.HasLineOfSight(2.5, 15.5, 8.5, 15.5, 0) ||
		g.HasLineOfSight(0.5, 0.5, 1.5, 0.5, 1) ||
		g.HasLineOfSight(-0.5, 15.5, 1.5, 15.5, 0) {
		t.Fatal("line-of-sight clearance or map boundary changed")
	}
}
