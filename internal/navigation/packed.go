package navigation

import "errors"

var ErrPackedTerrain = errors.New("invalid packed terrain or coordinate mapping")

// GridFromPacked retains the supplied build's compressed terrain after its
// caller has verified the instance and world-to-grid mapping. It stores the left
// cell in the low nibble and treats zero as blocked. This function does not
// establish that the input came from the current game instance.
func GridFromPacked(area string, originX, originY, cellSize float64,
	packed []byte, bytesPerRow, rows int) (*Grid, error) {
	if bytesPerRow < 1 || bytesPerRow > 50000 || rows < 1 || rows > 100000 ||
		len(packed) > 64*1024*1024 ||
		len(packed) != bytesPerRow*rows {
		return nil, ErrPackedTerrain
	}
	g := &Grid{Area: area, OriginX: originX, OriginY: originY,
		CellSize: cellSize, Width: bytesPerRow * 2, Height: rows,
		TargetNavigation: true, PackedSource: true, packed: packed, packedStride: bytesPerRow}
	if !g.Valid() {
		return nil, ErrPackedTerrain
	}
	return g, nil
}

// PackedValue reads one raw nibble without expanding the full map. The result
// is diagnostic; walkability uses zero versus nonzero only.
func (g *Grid) PackedValue(cell Cell) (byte, bool) {
	if g == nil || g.packed == nil || !g.contains(cell) {
		return 0, false
	}
	value := g.packed[cell.Y*g.packedStride+cell.X/2]
	if cell.X&1 == 0 {
		return value & 15, true
	}
	return value >> 4, true
}
