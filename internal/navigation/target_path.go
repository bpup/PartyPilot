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
	return g.targetWaypoints(fromX, fromY, toX, toY, nil)
}

// TargetWaypointsAvoid treats cells within radius of the observed leader as
// unavailable throughout search and route simplification.
func (g *Grid) TargetWaypointsAvoid(fromX, fromY, toX, toY,
	avoidX, avoidY, radius float64) ([]Waypoint, bool) {
	if !finite(avoidX) || !finite(avoidY) || !finite(radius) || radius <= 0 {
		return nil, false
	}
	return g.targetWaypoints(fromX, fromY, toX, toY,
		&avoidRegion{x: avoidX, y: avoidY, radius: radius})
}

func (g *Grid) targetWaypoints(fromX, fromY, toX, toY float64,
	avoid *avoidRegion) ([]Waypoint, bool) {
	if !g.Valid() || !g.TargetNavigation ||
		!finite(fromX) || !finite(fromY) || !finite(toX) || !finite(toY) {
		return nil, false
	}
	access := newGridAccess(g)
	allowedCache := make(map[int]bool)
	allowed := func(cell Cell) bool {
		if !g.contains(cell) {
			return false
		}
		index := g.index(cell)
		if value, known := allowedCache[index]; known {
			return value
		}
		centerX, centerY := g.center(cell)
		value := access.clear(cell, 1) && !avoid.contains(centerX, centerY)
		allowedCache[index] = value
		return value
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
					if allowed(cell) {
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
	if g.lineOfSight(access, fromX, fromY, toX, toY, 1, avoid) {
		return []Waypoint{{toX, toY}}, true
	}
	startIndex, goalIndex := g.index(start), g.index(goal)
	if startIndex == goalIndex {
		if g.lineOfSight(access, fromX, fromY, toX, toY, 1, avoid) {
			return []Waypoint{{toX, toY}}, true
		}
		startX, startY := g.center(start)
		if g.hasClearFollowSegment(access, fromX, fromY, startX, startY, avoid) &&
			g.lineOfSight(access, startX, startY, toX, toY, 1, avoid) {
			return []Waypoint{{startX, startY}, {toX, toY}}, true
		}
		return nil, false
	}
	best := map[int]float64{startIndex: 0}
	previous := make(map[int]int)
	closed := make(map[int]struct{})
	queue := &routeQueue{{startIndex, math.Hypot(float64(start.X-goal.X), float64(start.Y-goal.Y))}}
	heap.Init(queue)
	steps := [...]Cell{{1, 0}, {-1, 0}, {0, 1}, {0, -1}, {1, 1}, {1, -1}, {-1, 1}, {-1, -1}}
	for examined := 0; queue.Len() > 0 && examined < 60000; examined++ {
		current := heap.Pop(queue).(routeEntry).index
		if _, seen := closed[current]; seen {
			continue
		}
		closed[current] = struct{}{}
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
			nextCell := Cell{X: nx, Y: ny}
			_, visited := closed[next]
			if !allowed(nextCell) || visited ||
				(delta.X != 0 && delta.Y != 0 &&
					(!allowed(Cell{X: nx, Y: y}) || !allowed(Cell{X: x, Y: ny}))) {
				continue
			}
			cost := 1.0
			if delta.X != 0 && delta.Y != 0 {
				cost = math.Sqrt2
			}
			candidate := best[current] + cost
			if prior, known := best[next]; known && candidate >= prior {
				continue
			}
			best[next], previous[next] = candidate, current
			heap.Push(queue, routeEntry{next,
				candidate + math.Hypot(float64(nx-goal.X), float64(ny-goal.Y))})
		}
	}
	if _, found := closed[goalIndex]; !found {
		return nil, false
	}
	var cells []Cell
	for index := goalIndex; ; {
		cells = append(cells, Cell{index % g.Width, index / g.Width})
		if index == startIndex {
			break
		}
		prior, known := previous[index]
		if !known {
			return nil, false
		}
		index = prior
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
	for index, checks := 0, 0; index < len(points)-1; {
		chosen := index + 1
		for next := len(points) - 1; next > chosen && checks < 8192; next-- {
			checks++
			if g.lineOfSight(access, points[index].X, points[index].Y,
				points[next].X, points[next].Y, 1, avoid) {
				chosen = next
				break
			}
		}
		simplified = append(simplified, points[chosen])
		index = chosen
	}
	if len(simplified) == 0 {
		return nil, false
	}
	if g.lineOfSight(access, fromX, fromY,
		simplified[0].X, simplified[0].Y, 1, avoid) {
		return simplified, true
	}
	startX, startY := g.center(start)
	if g.hasClearFollowSegment(access, fromX, fromY, startX, startY, avoid) &&
		g.lineOfSight(access, startX, startY,
			simplified[0].X, simplified[0].Y, 1, avoid) {
		return append([]Waypoint{{startX, startY}}, simplified...), true
	}
	return nil, false
}

func (g *Grid) targetDirection(fromX, fromY, toX, toY float64) (float64, float64, bool) {
	points, ok := g.TargetWaypoints(fromX, fromY, toX, toY)
	return directionFromWaypoints(points, fromX, fromY, ok)
}

// NextDirectionAvoid is available only for target-format terrain.
func (g *Grid) NextDirectionAvoid(fromX, fromY, toX, toY,
	avoidX, avoidY, radius float64) (float64, float64, bool) {
	if g == nil || !g.TargetNavigation {
		return 0, 0, false
	}
	points, ok := g.TargetWaypointsAvoid(fromX, fromY, toX, toY,
		avoidX, avoidY, radius)
	return directionFromWaypoints(points, fromX, fromY, ok)
}

func directionFromWaypoints(points []Waypoint, fromX, fromY float64, ok bool) (float64, float64, bool) {
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
