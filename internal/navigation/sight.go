package navigation

import "math"

// HasLineOfSight samples at half-cell intervals. Clearance expands every
// sample by a square cell radius; an out-of-bounds neighbor blocks the ray.
func (g *Grid) HasLineOfSight(fromX, fromY, toX, toY float64, clearance int) bool {
	if !g.Valid() || clearance < 0 || clearance > 8 ||
		!finite(fromX) || !finite(fromY) || !finite(toX) || !finite(toY) {
		return false
	}
	blocked := make([]bool, g.Width*g.Height)
	for _, cell := range g.Blocked {
		blocked[g.index(cell)] = true
	}
	fromCellX := (fromX-g.OriginX)/g.CellSize - 0.5
	fromCellY := (fromY-g.OriginY)/g.CellSize - 0.5
	toCellX := (toX-g.OriginX)/g.CellSize - 0.5
	toCellY := (toY-g.OriginY)/g.CellSize - 0.5
	steps := max(1, int(math.Ceil(math.Hypot(toCellX-fromCellX, toCellY-fromCellY)*2)))
	for step := 0; step <= steps; step++ {
		fraction := float64(step) / float64(steps)
		x := int(math.Round(fromCellX + (toCellX-fromCellX)*fraction))
		y := int(math.Round(fromCellY + (toCellY-fromCellY)*fraction))
		for dy := -clearance; dy <= clearance; dy++ {
			for dx := -clearance; dx <= clearance; dx++ {
				cell := Cell{X: x + dx, Y: y + dy}
				if !g.contains(cell) || blocked[g.index(cell)] {
					return false
				}
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
