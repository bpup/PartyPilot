package combat

import (
	"testing"
	"time"
)

func TestDefaultTargetAssignments(t *testing.T) {
	profile := DefaultProfile()
	profile.Enabled = true
	world := testWorld()
	world.Monsters[0].Rarity = "rare"
	world.Monsters[1].Rarity = "unique"
	world.Monsters[2].Rarity = "rare"
	plans, err := Evaluate(profile, world)
	if err != nil {
		t.Fatal(err)
	}
	var targets TargetCoordinator
	targets.Assign(profile, world, plans)
	if plans[0].TargetID != "b" || plans[1].TargetID != "a" ||
		plans[2].TargetID != "c" || plans[3].TargetID != "b" ||
		plans[4].TargetID != "" || plans[5].TargetID != "b" {
		t.Fatalf("default role target strategies differ from source: %+v", plans)
	}
}

func TestTargetClaimsExpireAndDeadTargetsRelease(t *testing.T) {
	profile := DefaultProfile()
	profile.Enabled = true
	world := testWorld()
	var targets TargetCoordinator
	assign := func() []Plan {
		t.Helper()
		plans, err := Evaluate(profile, world)
		if err != nil {
			t.Fatal(err)
		}
		targets.Assign(profile, world, plans)
		return plans
	}
	if plans := assign(); plans[0].TargetID != "c" {
		t.Fatalf("main should prefer magic threat: %+v", plans[0])
	}
	world.Monsters[0].Rarity = "unique"
	world.Timestamp = world.Timestamp.Add(100 * time.Millisecond)
	if plans := assign(); plans[0].TargetID != "c" {
		t.Fatalf("live target claim should remain until expiry: %+v", plans[0])
	}
	world.Timestamp = world.Timestamp.Add(401 * time.Millisecond)
	if plans := assign(); plans[0].TargetID != "a" {
		t.Fatalf("expired target claim should release unique target: %+v", plans[0])
	}
	world.Monsters[0].Alive = ptr(false)
	world.Timestamp = world.Timestamp.Add(10 * time.Millisecond)
	if plans := assign(); plans[0].TargetID != "" {
		t.Fatalf("dead unique target should release claim: %+v", plans[0])
	}
}
