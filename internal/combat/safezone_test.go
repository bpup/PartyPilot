package combat

import "testing"

func TestKnownSafeZoneMatchesSuppliedAreaRule(t *testing.T) {
	for _, item := range []struct {
		id, label string
		want      bool
	}{
		{"HideoutArenaIncursion", "Red Queen's Chambers Hideout", true},
		{"Act1Town", "Clearfell Encampment", true},
		{"G1_town", "Clearfell Encampment", true},
		{"HideoutCanal", "Canal Hideout", true},
		{"MapSeepage", "Seepage", false},
		{"Delirium_Act1Town", "Simulacrum of Delusion", false},
		{"Delirium_Act1Town", "", false},
		{"", "", false},
	} {
		if got := IsKnownSafeZone(item.id, item.label); got != item.want {
			t.Fatalf("area %q %q: got %v, want %v", item.id, item.label, got, item.want)
		}
	}
}

func TestWorldRejectsContradictorySafeZoneClaim(t *testing.T) {
	world := testWorld()
	world.AreaID = "Act1Town"
	world.AreaLabel = "Clearfell Encampment"
	if err := world.Validate(); err == nil {
		t.Fatal("known safe zone accepted an asserted combat-safe world")
	}
	world.SafeZone = ptr(true)
	if err := world.Validate(); err != nil {
		t.Fatalf("safe-zone world should remain usable for blocked replay: %v", err)
	}
	world.SafeZone = ptr(false)
	world.AreaID = "Delirium_Act1Town"
	world.AreaLabel = "Simulacrum of Delusion"
	if err := world.Validate(); err != nil {
		t.Fatalf("Delirium combat area was mistaken for a safe zone: %v", err)
	}
}
