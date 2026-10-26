package navigation

type gridAccess struct {
	grid    *Grid
	blocked map[int]struct{}
}

func newGridAccess(grid *Grid) gridAccess {
	access := gridAccess{grid: grid}
	if grid.packed == nil {
		access.blocked = make(map[int]struct{}, len(grid.Blocked))
		for _, cell := range grid.Blocked {
			access.blocked[grid.index(cell)] = struct{}{}
		}
	}
	return access
}

func (access gridAccess) blockedAt(cell Cell) bool {
	if !access.grid.contains(cell) {
		return true
	}
	if access.grid.packed != nil {
		value, _ := access.grid.PackedValue(cell)
		return value == 0
	}
	_, blocked := access.blocked[access.grid.index(cell)]
	return blocked
}

func (access gridAccess) clear(cell Cell, radius int) bool {
	for dy := -radius; dy <= radius; dy++ {
		for dx := -radius; dx <= radius; dx++ {
			if access.blockedAt(Cell{X: cell.X + dx, Y: cell.Y + dy}) {
				return false
			}
		}
	}
	return true
}
