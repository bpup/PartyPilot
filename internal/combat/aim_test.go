package combat

import "testing"

func TestTargetAimUsesVerifiedPositionsAndAliveTarget(t *testing.T) {
	world := testWorld()
	world.Monsters[0].Position = &Point{X: 0, Y: 20}
	if got := AimAt(world, 1, "a"); got == nil || *got != (Aim{RightX: 0, RightY: 32767}) {
		t.Fatalf("target direction did not match the right stick: %+v", got)
	}
	player := world.Players[1]
	player.Position = nil
	world.Players[1] = player
	if got := AimAt(world, 1, "a"); got != nil {
		t.Fatalf("missing player position created an aim: %+v", got)
	}
	player.Position = &Point{X: 0, Y: 20}
	world.Players[1] = player
	if got := AimAt(world, 1, "a"); got == nil || *got != (Aim{}) {
		t.Fatalf("coincident target did not neutralize the right stick: %+v", got)
	}
	world.Monsters[0].Alive = ptr(false)
	if got := AimAt(world, 1, "a"); got != nil {
		t.Fatalf("dead target retained right-stick aim: %+v", got)
	}
}

func TestTargetCoordinatorAnnotatesReadOnlyAim(t *testing.T) {
	profile := DefaultProfile()
	profile.Enabled = true
	world := testWorld()
	plans, err := Evaluate(profile, world)
	if err != nil {
		t.Fatal(err)
	}
	var coordinator TargetCoordinator
	coordinator.Assign(profile, world, plans)
	if plans[0].Aim == nil || *plans[0].Aim != (Aim{RightX: 32767, RightY: 0}) {
		t.Fatalf("selected target has no read-only aim: %+v", plans[0])
	}
	player := world.Players[1]
	player.Position = nil
	world.Players[1] = player
	coordinator.Assign(profile, world, plans)
	if plans[0].Aim != nil {
		t.Fatalf("unverified player position kept the old aim: %+v", plans[0])
	}
}
