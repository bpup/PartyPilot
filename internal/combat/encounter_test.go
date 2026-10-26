package combat

import (
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
