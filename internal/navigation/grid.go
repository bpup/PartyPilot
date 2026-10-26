package navigation

import (
	"bytes"
	"math"
	"slices"
)

// Grid is a controlled, instance-specific navigation input. Its cells must
// come from a verified source before it can be used with live game data.
type Grid struct {
	Area             string  `json:"area"`
	OriginX          float64 `json:"origin_x"`
	OriginY          float64 `json:"origin_y"`
	CellSize         float64 `json:"cell_size"`
	Width            int     `json:"width"`
	Height           int     `json:"height"`
	Blocked          []Cell  `json:"blocked"`
	TargetNavigation bool    `json:"target_navigation,omitempty"`
	PackedSource     bool    `json:"packed_source,omitempty"`
	packed           []byte
	packedStride     int
}

type Cell struct {
	X int `json:"x"`
	Y int `json:"y"`
}

// Equivalent lets a replay retain one immutable map identity across JSON
// frames, which is required for target-style route caching and queued search.
func (g *Grid) Equivalent(other *Grid) bool {
	if g == nil || other == nil {
		return g == other
	}
	return g.Area == other.Area && g.OriginX == other.OriginX &&
		g.OriginY == other.OriginY && g.CellSize == other.CellSize &&
		g.Width == other.Width && g.Height == other.Height &&
		g.TargetNavigation == other.TargetNavigation &&
		g.PackedSource == other.PackedSource &&
		g.packedStride == other.packedStride &&
		bytes.Equal(g.packed, other.packed) && slices.Equal(g.Blocked, other.Blocked)
}

func finite(value float64) bool { return !math.IsNaN(value) && !math.IsInf(value, 0) }

func (g *Grid) Valid() bool {
	if g == nil || g.Area == "" || !finite(g.OriginX) || !finite(g.OriginY) ||
		!finite(g.CellSize) || g.CellSize <= 0 || g.Width < 1 || g.Height < 1 {
		return false
	}
	if !finite(g.OriginX+float64(g.Width-1)*g.CellSize) ||
		!finite(g.OriginY+float64(g.Height-1)*g.CellSize) {
		return false
	}
	if g.packed != nil {
		return g.TargetNavigation && g.PackedSource &&
			g.Width <= 100000 && g.Height <= 100000 &&
			g.packedStride > 0 && g.Width == g.packedStride*2 &&
			len(g.packed) <= 64*1024*1024 &&
			len(g.packed) == g.packedStride*g.Height && len(g.Blocked) == 0
	}
	if g.PackedSource {
		return false
	}
	if g.Width > 256 || g.Height > 256 || g.Width*g.Height > 65536 {
		return false
	}
	for _, cell := range g.Blocked {
		if !g.contains(cell) {
			return false
		}
	}
	return true
}

func (g *Grid) contains(cell Cell) bool {
	return cell.X >= 0 && cell.Y >= 0 && cell.X < g.Width && cell.Y < g.Height
}

func (g *Grid) cellAt(x, y float64) (Cell, bool) {
	if !finite(x) || !finite(y) {
		return Cell{}, false
	}
	if g.TargetNavigation {
		cell := Cell{X: int(math.RoundToEven((x - g.OriginX) / g.CellSize)),
			Y: int(math.RoundToEven((y - g.OriginY) / g.CellSize))}
		return cell, g.contains(cell)
	}
	cell := Cell{X: int(math.Floor((x - g.OriginX) / g.CellSize)),
		Y: int(math.Floor((y - g.OriginY) / g.CellSize))}
	return cell, g.contains(cell)
}

func (g *Grid) center(cell Cell) (float64, float64) {
	if g.TargetNavigation {
		return g.OriginX + float64(cell.X)*g.CellSize,
			g.OriginY + float64(cell.Y)*g.CellSize
	}
	return g.OriginX + (float64(cell.X)+0.5)*g.CellSize,
		g.OriginY + (float64(cell.Y)+0.5)*g.CellSize
}

func (g *Grid) index(cell Cell) int { return cell.Y*g.Width + cell.X }

// Path returns the shortest four-neighbor cell path. It never crosses a
// blocked cell or cuts diagonally across obstacle corners.
func (g *Grid) Path(fromX, fromY, toX, toY float64) ([]Cell, bool) {
	if !g.Valid() || g.TargetNavigation {
		return nil, false
	}
	start, startOK := g.cellAt(fromX, fromY)
	goal, goalOK := g.cellAt(toX, toY)
	if !startOK || !goalOK {
		return nil, false
	}
	count := g.Width * g.Height
	blocked := make([]bool, count)
	for _, cell := range g.Blocked {
		blocked[g.index(cell)] = true
	}
	startIndex, goalIndex := g.index(start), g.index(goal)
	if blocked[startIndex] || blocked[goalIndex] {
		return nil, false
	}
	previous := make([]int, count)
	for index := range previous {
		previous[index] = -1
	}
	queue := make([]int, 1, count)
	queue[0] = startIndex
	previous[startIndex] = startIndex
	for head := 0; head < len(queue) && previous[goalIndex] == -1; head++ {
		current := Cell{X: queue[head] % g.Width, Y: queue[head] / g.Width}
		for _, delta := range [...]Cell{{X: 1}, {Y: -1}, {X: -1}, {Y: 1}} {
			next := Cell{X: current.X + delta.X, Y: current.Y + delta.Y}
			if !g.contains(next) {
				continue
			}
			index := g.index(next)
			if blocked[index] || previous[index] != -1 {
				continue
			}
			previous[index] = queue[head]
			queue = append(queue, index)
		}
	}
	if previous[goalIndex] == -1 {
		return nil, false
	}
	var path []Cell
	for index := goalIndex; ; index = previous[index] {
		path = append(path, Cell{X: index % g.Width, Y: index / g.Width})
		if index == startIndex {
			break
		}
	}
	for left, right := 0, len(path)-1; left < right; left, right = left+1, right-1 {
		path[left], path[right] = path[right], path[left]
	}
	return path, true
}

func (g *Grid) NextDirection(fromX, fromY, toX, toY float64) (float64, float64, bool) {
	if g != nil && g.TargetNavigation {
		return g.targetDirection(fromX, fromY, toX, toY)
	}
	path, ok := g.Path(fromX, fromY, toX, toY)
	if !ok {
		return 0, 0, false
	}
	targetX, targetY := toX, toY
	if len(path) > 1 {
		targetX, targetY = g.center(path[1])
	}
	dx, dy := targetX-fromX, targetY-fromY
	length := math.Hypot(dx, dy)
	if length == 0 {
		return 0, 0, true
	}
	return dx / length, dy / length, true
}
