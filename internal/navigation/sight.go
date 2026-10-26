package navigation

import "math"

// HasLineOfSight samples at half-cell intervals. Clearance expands every
// sample by a square cell radius; an out-of-bounds neighbor blocks the ray.
func (g *Grid) HasLineOfSight(fromX, fromY, toX, toY float64, clearance int) bool {
	if !g.Valid() || clearance < 0 || clearance > 8 ||
		!finite(fromX) || !finite(fromY) || !finite(toX) || !finite(toY) {
		return false
	}
	return g.lineOfSight(newGridAccess(g), fromX, fromY, toX, toY, clearance, nil)
}

// HasClearFollowSegmentAvoid mirrors the supplied build's short escape from
// an obstructed starting cell. The leader exclusion applies to both checks.
func (g *Grid) HasClearFollowSegmentAvoid(fromX, fromY, toX, toY,
	avoidX, avoidY, radius float64) bool {
	if !g.Valid() || !g.TargetNavigation || !finite(avoidX) || !finite(avoidY) ||
		!finite(radius) || radius <= 0 {
		return false
	}
	access := newGridAccess(g)
	avoid := &avoidRegion{x: avoidX, y: avoidY, radius: radius}
	return g.hasClearFollowSegment(access, fromX, fromY, toX, toY, avoid)
}

func (g *Grid) hasClearFollowSegment(access gridAccess, fromX, fromY, toX, toY float64,
	avoid *avoidRegion) bool {
	if g.lineOfSight(access, fromX, fromY, toX, toY, 1, avoid) {
		return true
	}
	return math.Hypot(toX-fromX, toY-fromY) <= 4*g.CellSize &&
		g.lineOfSight(access, fromX, fromY, fromX, fromY, 0, avoid) &&
		!g.lineOfSight(access, fromX, fromY, fromX, fromY, 1, avoid) &&
		g.lineOfSight(access, toX, toY, toX, toY, 1, avoid) &&
		g.lineOfSight(access, fromX, fromY, toX, toY, 0, avoid)
}

// AdvanceTargetRoute skips reached points and takes the furthest visible
// remaining point, matching the target's route reuse rule.
func (g *Grid) AdvanceTargetRoute(route []Waypoint, fromX, fromY,
	avoidX, avoidY, radius float64) []Waypoint {
	if !g.Valid() || !g.TargetNavigation || len(route) == 0 ||
		!finite(fromX) || !finite(fromY) || !finite(avoidX) || !finite(avoidY) ||
		!finite(radius) || radius <= 0 {
		return nil
	}
	access := newGridAccess(g)
	avoid := &avoidRegion{x: avoidX, y: avoidY, radius: radius}
	i := 0
	for i < len(route)-1 && math.Hypot(fromX-route[i].X, fromY-route[i].Y) <= 3 &&
		g.lineOfSight(access, fromX, fromY, route[i+1].X, route[i+1].Y, 1, avoid) {
		i++
	}
	for candidate := len(route) - 1; candidate > i; candidate-- {
		if g.lineOfSight(access, fromX, fromY,
			route[candidate].X, route[candidate].Y, 1, avoid) {
			i = candidate
			break
		}
	}
	return append([]Waypoint(nil), route[i:]...)
}

type avoidRegion struct {
	x, y, radius float64
}

func (region *avoidRegion) contains(x, y float64) bool {
	return region != nil && math.Hypot(x-region.x, y-region.y) < region.radius
}

func (g *Grid) lineOfSight(access gridAccess, fromX, fromY, toX, toY float64,
	clearance int, avoid *avoidRegion) bool {
	if !finite(fromX) || !finite(fromY) || !finite(toX) || !finite(toY) || clearance < 0 {
		return false
	}
	shift := 0.5
	if g.TargetNavigation {
		shift = 0
	}
	fromCellX := (fromX-g.OriginX)/g.CellSize - shift
	fromCellY := (fromY-g.OriginY)/g.CellSize - shift
	toCellX := (toX-g.OriginX)/g.CellSize - shift
	toCellY := (toY-g.OriginY)/g.CellSize - shift
	if !finite(fromCellX) || !finite(fromCellY) || !finite(toCellX) || !finite(toCellY) ||
		fromCellX < -1 || fromCellY < -1 || toCellX < -1 || toCellY < -1 ||
		fromCellX > float64(g.Width) || fromCellY > float64(g.Height) ||
		toCellX > float64(g.Width) || toCellY > float64(g.Height) {
		return false
	}
	steps := max(1, int(math.Ceil(math.Hypot(toCellX-fromCellX, toCellY-fromCellY)*2)))
	for step := 0; step <= steps; step++ {
		fraction := float64(step) / float64(steps)
		x := int(math.Round(fromCellX + (toCellX-fromCellX)*fraction))
		y := int(math.Round(fromCellY + (toCellY-fromCellY)*fraction))
		if !access.clear(Cell{X: x, Y: y}, clearance) {
			return false
		}
		if avoid != nil {
			centerX, centerY := g.center(Cell{X: x, Y: y})
			if avoid.contains(centerX, centerY) {
				return false
			}
		}
	}
	return true
}

// SprintCollisionImminent mirrors the supplied build's three forward rays.
// The caller must first verify that the grid matches the current instance.
func (g *Grid) SprintCollisionImminent(fromX, fromY, toX, toY float64) bool {
	if !g.Valid() || !finite(fromX) || !finite(fromY) || !finite(toX) || !finite(toY) {
		return false
	}
	dx, dy := toX-fromX, toY-fromY
	distance := math.Hypot(dx, dy)
	if distance < 8 {
		return false
	}
	dx, dy = dx/distance, dy/distance
	startX, startY := fromX+dx*2, fromY+dy*2
	endX, endY := fromX+dx*math.Min(14, distance), fromY+dy*math.Min(14, distance)
	if g.HasLineOfSight(startX, startY, endX, endY, 0) {
		return false
	}
	offsetX, offsetY := -dy*g.CellSize*3, dx*g.CellSize*3
	return !g.HasLineOfSight(startX+offsetX, startY+offsetY,
		endX+offsetX, endY+offsetY, 0) &&
		!g.HasLineOfSight(startX-offsetX, startY-offsetY,
			endX-offsetX, endY-offsetY, 0)
}
