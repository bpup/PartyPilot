package combat

import (
	"encoding/json"
	"testing"
	"time"
)

func TestEncounterEntryAndExitDebounce(t *testing.T) {
	world := testWorld()
	var tracker EncounterTracker
	step := func(elapsed time.Duration, want string) *Encounter {
		t.Helper()
		world.Timestamp = testWorld().Timestamp.Add(elapsed)
		encounter, err := tracker.Update(world)
		if err != nil || encounter == nil || encounter.State != want {
			t.Fatalf("at %s wanted %s, got %+v, %v", elapsed, want, encounter, err)
		}
		return encounter
	}
	first := step(0, "approaching")
	step(149*time.Millisecond, "approaching")
	step(150*time.Millisecond, "engaged")
	step(200*time.Millisecond, "active")
	world.Monsters = []Monster{}
	winding := step(300*time.Millisecond, "winding-down")
	profile := DefaultProfile()
	profile.Enabled = true
	plans, err := EvaluateEncounter(profile, world, winding)
	if err != nil || plans[0].Status != "waiting_fight_ending" || plans[0].ComboID != "" {
		t.Fatalf("winding-down encounter should not plan a combo: %+v, %v", plans, err)
	}
	step(1099*time.Millisecond, "winding-down")
	step(1100*time.Millisecond, "complete")
	world.Timestamp = testWorld().Timestamp.Add(1200 * time.Millisecond)
	if encounter, err := tracker.Update(world); err != nil || encounter != nil {
		t.Fatalf("completed encounter should clear on following frame: %+v, %v", encounter, err)
	}
	world = testWorld()
	world.Timestamp = world.Timestamp.Add(1300 * time.Millisecond)
	second, err := tracker.Update(world)
	if err != nil || second == nil || second.ID == first.ID || second.State != "approaching" {
		t.Fatalf("new encounter should get a new ID: %+v, %v", second, err)
	}
}

func TestMonsterPositionRequiresBothCoordinates(t *testing.T) {
	for _, raw := range []string{`{}`, `{"x":1}`, `{"x":null,"y":2}`, `{"x":1,"y":2,"z":3}`} {
		var point Point
		if err := json.Unmarshal([]byte(raw), &point); err == nil {
			t.Fatalf("incomplete position accepted: %s", raw)
		}
	}
	var point Point
	if err := json.Unmarshal([]byte(`{"x":0,"y":2}`), &point); err != nil || point != (Point{X: 0, Y: 2}) {
		t.Fatalf("explicit zero coordinate rejected: %+v, %v", point, err)
	}
}

func TestEncounterThreatAndTagScope(t *testing.T) {
	world := testWorld()
	world.Monsters = world.Monsters[:1]
	var tracker EncounterTracker
	if encounter, err := tracker.Update(world); err != nil || encounter != nil {
		t.Fatalf("one normal monster should not start an encounter: %+v, %v", encounter, err)
	}
	world.Monsters[0].Rarity = "rare"
	encounter, err := tracker.Update(world)
	if err != nil || encounter == nil || !encounter.hasTag("rare-present") || !encounter.hasTag("isolated-threat") {
		t.Fatalf("one rare should start an encounter: %+v, %v", encounter, err)
	}
	world = testWorld()
	world.Monsters[0].Rarity = "rare"
	world.Monsters[1].Rarity = "unique"
	world.Monsters[2].Rarity = "rare"
	tracker = EncounterTracker{}
	encounter, err = tracker.Update(world)
	if err != nil || encounter == nil || !encounter.hasTag("boss") ||
		!encounter.hasTag("multi-rare") || !encounter.hasTag("dangerous") {
		t.Fatalf("mixed encounter tags changed: %+v, %v", encounter, err)
	}
	world = testWorld()
	world.Monsters = append(world.Monsters, Monster{ID: "far-boss", Rarity: "unique", Alive: ptr(true),
		Position:          &Point{X: 200, Y: 0},
		DistanceFromMainM: ptr(200.0), DistanceByPlayer: map[int]float64{1: 200, 2: 200, 3: 200,
			4: 200, 5: 200, 6: 200}, HealthPercent: ptr(100.0)})
	tracker = EncounterTracker{}
	encounter, err = tracker.Update(world)
	if err != nil || encounter == nil || encounter.hasTag("boss") {
		t.Fatalf("out-of-range unique should not tag the encounter: %+v, %v", encounter, err)
	}
	profile := DefaultProfile()
	profile.Enabled = true
	plans, err := EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[0].ComboID != "primary-clear" {
		t.Fatalf("remote unique should not change pack plan: %+v, %v", plans, err)
	}
}

func TestFormationBrokenUsesExplicitPlayerDistance(t *testing.T) {
	world := testWorld()
	player := world.Players[2]
	player.DistanceFromMainM = ptr(15.0)
	world.Players[2] = player
	var tracker EncounterTracker
	encounter, err := tracker.Update(world)
	if err != nil || encounter == nil || encounter.hasTag("formation-broken") {
		t.Fatalf("15 m must not break formation: %+v, %v", encounter, err)
	}
	player.DistanceFromMainM = ptr(15.01)
	world.Players[2] = player
	world.Timestamp = world.Timestamp.Add(time.Millisecond)
	encounter, err = tracker.Update(world)
	if err != nil || encounter == nil || !encounter.hasTag("formation-broken") {
		t.Fatalf("distance beyond 15 m must break formation: %+v, %v", encounter, err)
	}
	player.DistanceFromMainM = nil
	world.Players[2] = player
	if _, err := tracker.Update(world); err == nil {
		t.Fatal("missing player distance was accepted")
	}
}

func TestEncounterVersionTracksSourceChanges(t *testing.T) {
	world := testWorld()
	world.Monsters[2].Rarity = "normal"
	var tracker EncounterTracker
	step := func(ms int, want int) {
		t.Helper()
		world.Timestamp = testWorld().Timestamp.Add(time.Duration(ms) * time.Millisecond)
		encounter, err := tracker.Update(world)
		if err != nil || encounter == nil || encounter.Version != want {
			t.Fatalf("at %d ms wanted version %d, got %+v, %v", ms, want, encounter, err)
		}
	}
	step(0, 2) // Initial manager version, then Pack tag.
	step(1, 2) // No structural or tag change.
	world.Monsters[1].DistanceByPlayer[1] = 19
	step(2, 3) // Primary threat changes with the same IDs and counts.
	world.Monsters[2].Rarity = "magic"
	step(3, 4) // Rarity counts change; Pack tag remains.
	player := world.Players[2]
	player.DistanceFromMainM = ptr(16.0)
	world.Players[2] = player
	step(4, 5) // Only FormationBroken tag changes.
	step(5, 5)
	step(150, 6) // Approaching becomes engaged.
	world.Monsters[0].Rarity = "rare"
	step(151, 8) // Structural change and tag change each increment once.
}

func TestEncounterCentroidAndLifecycleTimes(t *testing.T) {
	world := testWorld()
	world.Monsters[0].Position = &Point{X: 10, Y: 2}
	world.Monsters[1].Position = &Point{X: 20, Y: 4}
	world.Monsters[2].Position = &Point{X: 30, Y: 6}
	start := world.Timestamp
	var tracker EncounterTracker
	encounter, err := tracker.Update(world)
	if err != nil || encounter == nil || encounter.Centroid != (Point{X: 20, Y: 4}) ||
		!encounter.StartedAt.Equal(start) || !encounter.UpdatedAt.Equal(start) || encounter.EngagedAt != nil {
		t.Fatalf("initial encounter geometry or time differs: %+v, %v", encounter, err)
	}
	world.Timestamp = start.Add(150 * time.Millisecond)
	world.Monsters[0].Position = &Point{X: 40, Y: 2}
	encounter, err = tracker.Update(world)
	if err != nil || encounter == nil || encounter.Centroid != (Point{X: 30, Y: 4}) ||
		encounter.EngagedAt == nil || !encounter.EngagedAt.Equal(world.Timestamp) ||
		!encounter.UpdatedAt.Equal(world.Timestamp) {
		t.Fatalf("engaged encounter geometry or time differs: %+v, %v", encounter, err)
	}
	world.Timestamp = start.Add(200 * time.Millisecond)
	world.Monsters = []Monster{}
	encounter, err = tracker.Update(world)
	if err != nil || encounter == nil || encounter.Centroid != (Point{}) ||
		encounter.WindingDownAt == nil || !encounter.WindingDownAt.Equal(world.Timestamp) {
		t.Fatalf("winding encounter geometry or time differs: %+v, %v", encounter, err)
	}
	world.Timestamp = start.Add(1000 * time.Millisecond)
	encounter, err = tracker.Update(world)
	if err != nil || encounter == nil || encounter.CompletedAt == nil ||
		!encounter.CompletedAt.Equal(world.Timestamp) || !encounter.StartedAt.Equal(start) {
		t.Fatalf("completed encounter time differs: %+v, %v", encounter, err)
	}
	world = testWorld()
	world.Monsters[0].Position = nil
	if _, err := tracker.Update(world); err == nil {
		t.Fatal("monster with missing position was accepted")
	}
}
