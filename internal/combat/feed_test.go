package combat

import (
	"testing"
	"time"
)

func orderedFeedFrame(client int, captured time.Time, localPlayers int) FeedFrame {
	frame := FeedFrame{Client: client, CapturedAt: captured,
		AreaInstance: "client-pointer", AreaID: "MapSeepage", AreaLabel: "Seepage",
		Attached: true, PlayerOrderReliable: true,
		UI:       UI{ptr(false), ptr(false), ptr(false), ptr(false), ptr(false), ptr(false)},
		Monsters: []FeedMonster{}}
	for local := 1; local <= localPlayers; local++ {
		frame.Players = append(frame.Players, FeedPlayer{LocalPlayer: local,
			Position: &Point{X: float32((client-1)*2 + local - 1), Y: 0},
			Alive:    ptr(true), HealthPercent: ptr(100.0)})
	}
	return frame
}

func TestFeedAssemblerMapsThreeOrderedClientsAndTargetDistances(t *testing.T) {
	now := time.Date(2026, 10, 27, 0, 0, 0, 0, time.UTC)
	frames := []FeedFrame{orderedFeedFrame(1, now.Add(-100*time.Millisecond), 2),
		orderedFeedFrame(2, now.Add(-110*time.Millisecond), 2),
		orderedFeedFrame(3, now.Add(-120*time.Millisecond), 2)}
	frames[0].Monsters = []FeedMonster{{ID: "rare-1", Rarity: "rare",
		Position: &Point{X: 10, Y: 0}, Alive: ptr(true), HealthPercent: ptr(60.0)}}
	frames[1].AreaInstance = "other-client-pointer"
	frames[2].AreaInstance = "third-client-pointer"
	engaged := map[int]bool{1: false, 2: true, 3: false, 4: false, 5: false, 6: false}
	world, err := AssembleWorld(frames, FeedAuthority{ExpectedClients: []int{1, 2, 3},
		EngagedPlayers: engaged, SafeZone: ptr(false), EmergencyActive: ptr(false)}, now)
	if err != nil {
		t.Fatal(err)
	}
	if len(world.Players) != 6 || *world.DataAgeMS != 100 ||
		*world.Players[2].ActiveCombo != true || *world.Players[6].DistanceFromMainM != 5 ||
		len(world.Monsters) != 1 || world.Monsters[0].DistanceByPlayer[6] != 5 ||
		world.Monsters[0].DistanceByPlayer[1] != 10 {
		t.Fatalf("three-client ordered world differs from source mapping: %+v", world)
	}
	if plans, err := Evaluate(DefaultProfile(), world); err != nil || len(plans) != 6 {
		t.Fatalf("assembled world could not enter the read-only planner: %+v, %v", plans, err)
	}
}

func TestFeedAssemblerRejectsMissingOrConflictingAuthority(t *testing.T) {
	now := time.Date(2026, 10, 27, 0, 0, 0, 0, time.UTC)
	first := orderedFeedFrame(1, now.Add(-100*time.Millisecond), 2)
	second := orderedFeedFrame(2, now.Add(-100*time.Millisecond), 1)
	authority := FeedAuthority{ExpectedClients: []int{1, 2},
		EngagedPlayers: map[int]bool{1: false, 2: false, 3: false},
		SafeZone:       ptr(false), EmergencyActive: ptr(false)}
	checkReject := func(frames []FeedFrame, auth FeedAuthority) {
		t.Helper()
		if _, err := AssembleWorld(frames, auth, now); err == nil {
			t.Fatalf("incomplete feed authority was accepted: %+v %+v", frames, auth)
		}
	}
	checkReject([]FeedFrame{first}, authority)
	missingMonsterScan := first
	missingMonsterScan.Monsters = nil
	checkReject([]FeedFrame{missingMonsterScan, second}, authority)
	unreliable := second
	unreliable.PlayerOrderReliable = false
	checkReject([]FeedFrame{first, unreliable}, authority)
	otherArea := second
	otherArea.AreaID = "MapElsewhere"
	checkReject([]FeedFrame{first, otherArea}, authority)
	missingEngagement := authority
	missingEngagement.EngagedPlayers = map[int]bool{1: false, 2: false}
	checkReject([]FeedFrame{first, second}, missingEngagement)
	gap := second
	gap.Players = []FeedPlayer{{LocalPlayer: 2, Position: &Point{X: 3}, Alive: ptr(true)}}
	checkReject([]FeedFrame{first, gap}, authority)
	missingUI := second
	missingUI.UI.Loading = nil
	checkReject([]FeedFrame{first, missingUI}, authority)
	knownTown := first
	knownTown.AreaID = "Act1Town"
	knownTown.AreaLabel = "Clearfell Encampment"
	checkReject([]FeedFrame{knownTown}, FeedAuthority{ExpectedClients: []int{1},
		EngagedPlayers: map[int]bool{1: false, 2: false},
		SafeZone:       ptr(false), EmergencyActive: ptr(false)})
}
