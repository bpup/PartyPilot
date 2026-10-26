package navigation

import (
	"container/heap"
	"math"
)

// Waypoint uses the caller's grid coordinate scale.
type Waypoint struct{ X, Y float64 }

type routeEntry struct {
	index    int
	priority float64
}
type routeQueue []routeEntry

func (q routeQueue) Len() int           { return len(q) }
func (q routeQueue) Less(i, j int) bool { return q[i].priority < q[j].priority }
func (q routeQueue) Swap(i, j int)      { q[i], q[j] = q[j], q[i] }
func (q *routeQueue) Push(value any)    { *q = append(*q, value.(routeEntry)) }
func (q *routeQueue) Pop() any {
	last := len(*q) - 1
	value := (*q)[last]
	*q = (*q)[:last]
	return value
}

// TargetWaypoints uses the supplied build's eight-neighbor search, one-cell
// clearance, diagonal corner guard and line-of-sight simplification. The grid
// still needs a verified area identity and coordinate mapping before live use.
func (g *Grid) TargetWaypoints(fromX, fromY, toX, toY float64) ([]Waypoint, bool) {
	if !g.Valid() || !g.TargetNavigation ||
		!finite(fromX) || !finite(fromY) || !finite(toX) || !finite(toY) {
		return nil, false
	}
	count := g.Width * g.Height
	blocked := make([]bool, count)
	for _, cell := range g.Blocked {
		blocked[g.index(cell)] = true
	}
	allowed := make([]bool, count)
	for y := 1; y < g.Height-1; y++ {
		for x := 1; x < g.Width-1; x++ {
			clear := true
			for dy := -1; dy <= 1 && clear; dy++ {
				for dx := -1; dx <= 1; dx++ {
					if blocked[(y+dy)*g.Width+x+dx] {
						clear = false
						break
					}
				}
			}
			allowed[y*g.Width+x] = clear
		}
	}
	nearest := func(px, py float64) (Cell, bool) {
		scaledX := (px - g.OriginX) / g.CellSize
		scaledY := (py - g.OriginY) / g.CellSize
		if !finite(scaledX) || !finite(scaledY) || scaledX < -12 || scaledY < -12 ||
			scaledX > float64(g.Width+12) || scaledY > float64(g.Height+12) {
			return Cell{}, false
		}
		baseX := int(math.RoundToEven(scaledX))
		baseY := int(math.RoundToEven(scaledY))
		for radius := 0; radius <= 12; radius++ {
			for y := baseY - radius; y <= baseY+radius; y++ {
				for x := baseX - radius; x <= baseX+radius; x++ {
					if radius > 0 && x != baseX-radius && x != baseX+radius &&
						y != baseY-radius && y != baseY+radius {
						continue
					}
					cell := Cell{X: x, Y: y}
					if g.contains(cell) && allowed[g.index(cell)] {
						return cell, true
					}
				}
			}
		}
		return Cell{}, false
	}
	start, startOK := nearest(fromX, fromY)
	goal, goalOK := nearest(toX, toY)
	if !startOK || !goalOK {
		return nil, false
	}
	if g.lineOfSight(blocked, fromX, fromY, toX, toY, 1) {
		return []Waypoint{{toX, toY}}, true
	}
	startIndex, goalIndex := g.index(start), g.index(goal)
	if startIndex == goalIndex {
		if g.lineOfSight(blocked, fromX, fromY, toX, toY, 0) {
			return []Waypoint{{toX, toY}}, true
		}
		return nil, false
	}
	best := make([]float64, count)
	previous := make([]int, count)
	closed := make([]bool, count)
	for i := range best {
		best[i] = math.Inf(1)
		previous[i] = -1
	}
	best[startIndex] = 0
	queue := &routeQueue{{startIndex, math.Hypot(float64(start.X-goal.X), float64(start.Y-goal.Y))}}
	heap.Init(queue)
	steps := [...]Cell{{1, 0}, {-1, 0}, {0, 1}, {0, -1}, {1, 1}, {1, -1}, {-1, 1}, {-1, -1}}
	for examined := 0; queue.Len() > 0 && examined < 60000; examined++ {
		current := heap.Pop(queue).(routeEntry).index
		if closed[current] {
			continue
		}
		closed[current] = true
		if current == goalIndex {
			break
		}
		x, y := current%g.Width, current/g.Width
		for _, delta := range steps {
			nx, ny := x+delta.X, y+delta.Y
			if nx < 0 || ny < 0 || nx >= g.Width || ny >= g.Height {
				continue
			}
			next := ny*g.Width + nx
			if !allowed[next] || closed[next] ||
				(delta.X != 0 && delta.Y != 0 &&
					(!allowed[y*g.Width+nx] || !allowed[ny*g.Width+x])) {
				continue
			}
			cost := 1.0
			if delta.X != 0 && delta.Y != 0 {
				cost = math.Sqrt2
			}
			candidate := best[current] + cost
			if candidate >= best[next] {
				continue
			}
			best[next], previous[next] = candidate, current
			heap.Push(queue, routeEntry{next,
				candidate + math.Hypot(float64(nx-goal.X), float64(ny-goal.Y))})
		}
	}
	if !closed[goalIndex] {
		return nil, false
	}
	var cells []Cell
	for index := goalIndex; index != -1; index = previous[index] {
		cells = append(cells, Cell{index % g.Width, index / g.Width})
	}
	for left, right := 0, len(cells)-1; left < right; left, right = left+1, right-1 {
		cells[left], cells[right] = cells[right], cells[left]
	}
	points := make([]Waypoint, len(cells))
	for i, cell := range cells {
		points[i].X, points[i].Y = g.center(cell)
	}
	points[len(points)-1] = Waypoint{toX, toY}
	var simplified []Waypoint
	current := Waypoint{fromX, fromY}
	for index, checks := 0, 0; index < len(points)-1; {
		chosen := -1
		for next := len(points) - 1; next > index && checks < 8192; next-- {
			checks++
			if g.lineOfSight(blocked, current.X, current.Y, points[next].X, points[next].Y, 1) {
				chosen = next
				break
			}
		}
		if chosen < 0 {
			chosen = index + 1
			if !g.lineOfSight(blocked, current.X, current.Y,
				points[chosen].X, points[chosen].Y, 0) {
				return nil, false
			}
		}
		current = points[chosen]
		simplified = append(simplified, current)
		index = chosen
	}
	return simplified, len(simplified) > 0
}

func (g *Grid) targetDirection(fromX, fromY, toX, toY float64) (float64, float64, bool) {
	points, ok := g.TargetWaypoints(fromX, fromY, toX, toY)
	if !ok || len(points) == 0 {
		return 0, 0, false
	}
	dx, dy := points[0].X-fromX, points[0].Y-fromY
	length := math.Hypot(dx, dy)
	if length == 0 {
		return 0, 0, true
	}
	return dx / length, dy / length, true
}
