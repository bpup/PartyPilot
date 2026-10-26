package navigation

import "testing"

func TestGridFromPackedKeepsNibbleOrderAndDetours(t *testing.T) {
	// Five rows, six cells per row. The middle column is blocked except
	// at the top and bottom, so a route must go around it.
	packed := []byte{
		0x11, 0x11, 0x11,
		0x11, 0x10, 0x11,
		0x11, 0x10, 0x11,
		0x11, 0x10, 0x11,
		0x11, 0x11, 0x11,
	}
	g, err := GridFromPacked("observed-instance", 10, 20, 1, packed, 3, 5)
	if err != nil || !g.Valid() {
		t.Fatalf("valid packed terrain rejected: %v", err)
	}
	if len(g.Blocked) != 3 || g.Blocked[0] != (Cell{X: 2, Y: 1}) {
		t.Fatalf("low nibble must be the left cell: %v", g.Blocked)
	}
	if _, ok := g.TargetWaypoints(10, 22, 15, 22); ok {
		t.Fatal("narrow legacy fixture was accepted despite one-cell clearance")
	}
	wide := make([]byte, 10*15)
	for i := range wide {
		wide[i] = 0x11
	}
	for y := 4; y <= 10; y++ {
		wide[y*10+5] = 0x10 // left nibble is cell x=10.
	}
	terrain, err := GridFromPacked("observed-instance", 0, 0, 1, wide, 10, 15)
	if err != nil || !terrain.TargetNavigation {
		t.Fatalf("target terrain mode was not set: %v", err)
	}
	waypoints, ok := terrain.TargetWaypoints(4, 7, 16, 7)
	if !ok || len(waypoints) == 0 || waypoints[0].Y == 7 ||
		waypoints[len(waypoints)-1] != (Waypoint{16, 7}) {
		t.Fatalf("packed terrain did not detour around cleared wall: %v, %v", waypoints, ok)
	}
}

func TestGridFromPackedRejectsIncompleteOrUnmappedInput(t *testing.T) {
	for _, tc := range []struct {
		area   string
		data   []byte
		stride int
		rows   int
	}{
		{"instance", []byte{0x11}, 1, 2},
		{"instance", []byte{0x11, 0x11}, 1, 1},
		{"", []byte{0x11}, 1, 1},
		{"instance", []byte{0x11}, 0, 1},
	} {
		if _, err := GridFromPacked(tc.area, 0, 0, 1, tc.data, tc.stride, tc.rows); err == nil {
			t.Fatalf("accepted invalid packed input: %+v", tc)
		}
	}
	if _, err := GridFromPacked("instance", 0, 0, 0, []byte{0x11}, 1, 1); err == nil {
		t.Fatal("accepted missing cell size")
	}
}
