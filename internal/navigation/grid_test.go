package navigation

import "testing"

func TestPathGoesAroundWallWithoutCrossingBlockedCells(t *testing.T) {
	grid := &Grid{Area: "instance", CellSize: 1, Width: 5, Height: 5,
		Blocked: []Cell{{2, 1}, {2, 2}, {2, 3}}}
	path, ok := grid.Path(0.5, 2.5, 4.5, 2.5)
	if !ok || len(path) != 9 || path[0] != (Cell{0, 2}) || path[len(path)-1] != (Cell{4, 2}) {
		t.Fatalf("missing detour: %v, %v", path, ok)
	}
	blocked := map[Cell]bool{{2, 1}: true, {2, 2}: true, {2, 3}: true}
	for index, cell := range path {
		if blocked[cell] {
			t.Fatalf("route crosses blocked cell %v", cell)
		}
		if index > 0 {
			previous := path[index-1]
			step := abs(cell.X-previous.X) + abs(cell.Y-previous.Y)
			if step != 1 {
				t.Fatalf("route skips cell: %v", path)
			}
		}
	}
}

func TestBlockedOrUnknownTerrainDoesNotProduceRoute(t *testing.T) {
	grid := &Grid{Area: "instance", CellSize: 1, Width: 5, Height: 5,
		Blocked: []Cell{{2, 0}, {2, 1}, {2, 2}, {2, 3}, {2, 4}}}
	if _, ok := grid.Path(0.5, 2.5, 4.5, 2.5); ok {
		t.Fatal("routed through complete wall")
	}
	if _, ok := grid.Path(-0.5, 2.5, 4.5, 2.5); ok {
		t.Fatal("routed from outside known terrain")
	}
	grid.Blocked = append(grid.Blocked, Cell{6, 0})
	if grid.Valid() {
		t.Fatal("accepted out-of-bounds obstacle")
	}
}

func abs(value int) int {
	if value < 0 {
		return -value
	}
	return value
}
