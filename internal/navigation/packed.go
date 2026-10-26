package navigation

import "errors"

var ErrPackedTerrain = errors.New("invalid packed terrain or coordinate mapping")

// GridFromPacked decodes a bounded terrain window after its caller has verified
// the instance and world-to-grid mapping. The supplied build stores the left
// cell in the low nibble and treats zero as blocked. This function does not
// establish that the input came from the current game instance.
func GridFromPacked(area string, originX, originY, cellSize float64,
	packed []byte, bytesPerRow, rows int) (*Grid, error) {
	if bytesPerRow < 1 || bytesPerRow > 128 || rows < 1 || rows > 256 ||
		len(packed) != bytesPerRow*rows {
		return nil, ErrPackedTerrain
	}
	g := &Grid{Area: area, OriginX: originX, OriginY: originY,
		CellSize: cellSize, Width: bytesPerRow * 2, Height: rows}
	if !g.Valid() {
		return nil, ErrPackedTerrain
	}
	for y := 0; y < rows; y++ {
		for x := 0; x < bytesPerRow; x++ {
			value := packed[y*bytesPerRow+x]
			if value&0x0f == 0 {
				g.Blocked = append(g.Blocked, Cell{X: x * 2, Y: y})
			}
			if value>>4 == 0 {
				g.Blocked = append(g.Blocked, Cell{X: x*2 + 1, Y: y})
			}
		}
	}
	return g, nil
}
