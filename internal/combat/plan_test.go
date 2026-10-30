package combat

import (
	"testing"
	"time"
)

func ptr[T any](value T) *T { return &value }

func testWorld() World {
	players := make(map[int]Player)
	for number := 1; number <= 6; number++ {
		players[number] = Player{Alive: ptr(true), Loading: ptr(false), ActiveCombo: ptr(false),
			Position: &Point{X: 0, Y: 0}, DistanceFromMainM: ptr(0.0), HealthPercent: ptr(100.0)}
	}
	monster := func(id, rarity string) Monster {
		distance := make(map[int]float64)
		for number := 1; number <= 6; number++ {
			distance[number] = 20
		}
		return Monster{ID: id, Rarity: rarity, Alive: ptr(true), Position: &Point{X: 20, Y: 0},
			DistanceFromMainM: ptr(20.0), DistanceByPlayer: distance, HealthPercent: ptr(100.0)}
	}
	return World{Timestamp: time.Date(2026, 10, 27, 0, 0, 0, 0, time.UTC),
		AreaInstance: "synthetic-area", DataAgeMS: ptr(100), SafeZone: ptr(false),
		EmergencyActive: ptr(false),
		UI:              UI{ptr(false), ptr(false), ptr(false), ptr(false), ptr(false), ptr(false)},
		Players:         players, Monsters: []Monster{monster("a", "normal"), monster("b", "normal"),
			monster("c", "magic")}}
}

func TestDefaultProfileAndReadOnlyBehavior(t *testing.T) {
	profile := DefaultProfile()
	if profile.Enabled || len(profile.Roles) != 6 || len(profile.Combos) != 18 ||
		profile.Safety.StaleDataTimeoutMS != 750 || profile.Roles[5].Name != "Rarity Finisher" {
		t.Fatal("supplied 0.6.17 default structure changed")
	}
	world := testWorld()
	plans, err := Evaluate(profile, world)
	if err != nil || plans[0].Status != "profile_disabled" {
		t.Fatalf("default disabled gate changed: %+v, %v", plans, err)
	}
	profile.Enabled = true
	plans, err = Evaluate(profile, world)
	if err != nil || plans[0].ComboID != "primary-clear" ||
		plans[1].ComboID != "companion-rally-clear" ||
		plans[4].ComboID != "defensive-standby" ||
		plans[5].ComboID != "safe-follow" || plans[0].Status != "planned_only" {
		t.Fatalf("pack roles changed: %+v, %v", plans, err)
	}
	for index, want := range []int{219, 359, 459, 645, 720, 965} {
		if plans[index].ScheduledOffsetMS == nil || *plans[index].ScheduledOffsetMS != want {
			t.Fatalf("P%d default pacing differs from source: %+v", index+1, plans[index])
		}
	}
	world.Monsters[0].Rarity = "rare"
	plans, err = Evaluate(profile, world)
	if err != nil || plans[0].ComboID != "rare-dps" || plans[3].ComboID != "rare-support" {
		t.Fatalf("rare roles changed: %+v, %v", plans, err)
	}
	world.Monsters[1].Rarity = "unique"
	plans, err = Evaluate(profile, world)
	if err != nil || plans[0].ComboID != "boss-engage" ||
		plans[5].ComboID != "finisher-ready" {
		t.Fatalf("boss roles changed: %+v, %v", plans, err)
	}
	world.Monsters[2].Rarity = "rare"
	plans, err = Evaluate(profile, world)
	if err != nil || plans[1].ComboID != "companion-rally-rare" {
		t.Fatalf("companion multi-rare split changed: %+v, %v", plans, err)
	}
	world.EmergencyActive = ptr(true)
	plans, err = Evaluate(profile, world)
	if err != nil || plans[0].ComboID != "emergency-defence" ||
		plans[1].ComboID != "companion-recovery" {
		t.Fatalf("emergency roles changed: %+v, %v", plans, err)
	}
}

func TestEmergencyDoesNotRequireAnEnemyNearTheRole(t *testing.T) {
	profile := DefaultProfile()
	profile.Enabled = true
	world := testWorld()
	world.EmergencyActive = ptr(true)
	for index := range world.Monsters {
		world.Monsters[index].DistanceByPlayer[2] = 120
	}
	encounter := &Encounter{ID: "enc-1", State: "active", Tags: []string{"pack"}}
	plans, err := EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].Status != "planned_only" || plans[1].Behavior != "emergency" ||
		plans[1].ComboID != "companion-recovery" {
		t.Fatalf("emergency recovery should survive an out-of-range role: %+v, %v", plans, err)
	}
}

func TestRoleEligibilityFollowsSuppliedResolver(t *testing.T) {
	profile := DefaultProfile()
	profile.Enabled = true
	world := testWorld()
	encounter := &Encounter{ID: "enc-1", State: "active", Tags: []string{"pack"}}
	role := &profile.Roles[1]
	player := world.Players[2]
	player.DistanceFromMainM = ptr(25.0)
	world.Players[2] = player
	role.Logic.MaximumLeashM = 20
	plans, err := EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].Status != "waiting_no_eligible_encounter" ||
		plans[1].Reason != "Waiting: too far from MAIN (25 of 20)" {
		t.Fatalf("role leash was ignored: %+v, %v", plans, err)
	}
	world.EmergencyActive = ptr(true)
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].ComboID != "" {
		t.Fatalf("emergency bypassed the role leash: %+v, %v", plans, err)
	}
	world.EmergencyActive = ptr(false)
	player.DistanceFromMainM = ptr(10.0)
	world.Players[2] = player
	role.Logic.StartHelpingAtHealthPercent = 50
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].Reason != "Waiting: enemy above 50%" || plans[1].ComboID != "" {
		t.Fatalf("target health gate was ignored: %+v, %v", plans, err)
	}
	for index := range world.Monsters {
		world.Monsters[index].HealthPercent = ptr(40.0)
	}
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].ComboID != "companion-rally-clear" {
		t.Fatalf("qualifying target health did not restore planning: %+v, %v", plans, err)
	}
	role.Logic.AttackNormal = false
	role.Logic.AttackMagic = false
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].Reason != "Waiting: no enemies in range" || plans[1].ComboID != "" {
		t.Fatalf("rarity exclusions were ignored: %+v, %v", plans, err)
	}
	world.Monsters[0].Rarity = "rare"
	encounter.Tags = []string{"rare-present"}
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].ComboID != "companion-rally-rare" {
		t.Fatalf("allowed rare did not restore planning: %+v, %v", plans, err)
	}
	role.Logic.MaximumLeashM = -1
	if _, err := EvaluateEncounter(profile, world, encounter); err == nil {
		t.Fatal("negative role leash was accepted")
	}
	role.Logic.MaximumLeashM = 401
	if _, err := EvaluateEncounter(profile, world, encounter); err == nil {
		t.Fatal("leash above the supplied profile maximum was accepted")
	}
	role.Logic.MaximumLeashM = 0
	role.Logic.StartHelpingAtHealthPercent = 5
	if _, err := EvaluateEncounter(profile, world, encounter); err == nil {
		t.Fatal("help threshold below full aggression was accepted")
	}
}

func TestEngageAroundMainUsesCullerGate(t *testing.T) {
	profile := DefaultProfile()
	profile.Enabled = true
	profile.Roles[1].Logic.EngageAroundMain = true
	world := testWorld()
	encounter := &Encounter{ID: "enc-1", State: "active", Tags: []string{"pack"}}
	for index := range world.Monsters {
		world.Monsters[index].DistanceByPlayer[2] = 130
	}
	plans, err := EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].ComboID != "companion-rally-clear" {
		t.Fatalf("MAIN-centred pack with a reachable culler target was lost: %+v, %v", plans, err)
	}
	for index := range world.Monsters {
		world.Monsters[index].DistanceFromMainM = ptr(51.0)
	}
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].Reason != "Waiting: no enemy within Spark reach" {
		t.Fatalf("unreachable culler target was accepted: %+v, %v", plans, err)
	}
	world.EmergencyActive = ptr(true)
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].ComboID != "" ||
		plans[1].Reason != "Waiting: no enemy within Spark reach" {
		t.Fatalf("emergency skipped the culler pre-check: %+v, %v", plans, err)
	}
	world.EmergencyActive = ptr(false)
	for index := range world.Monsters {
		world.Monsters[index].DistanceFromMainM = ptr(20.0)
	}
	profile.Roles[1].Logic.MaximumLeashM = 10
	player := world.Players[2]
	player.DistanceFromMainM = ptr(80.0)
	world.Players[2] = player
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].Reason != "Waiting: too far from MAIN (80 of 70)" {
		t.Fatalf("idle culler band was ignored: %+v, %v", plans, err)
	}
	player.ActiveCombo = ptr(true)
	world.Players[2] = player
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].ComboID != "companion-rally-clear" {
		t.Fatalf("running culler band did not retain a combo: %+v, %v", plans, err)
	}
	player.DistanceFromMainM = ptr(115.0)
	world.Players[2] = player
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[1].Reason != "Waiting: too far from MAIN (115 of 110)" {
		t.Fatalf("running culler band overrun was accepted: %+v, %v", plans, err)
	}
}

func TestPacingUsesCandidateOrderRatherThanPlayerNumber(t *testing.T) {
	profile := DefaultProfile()
	profile.Enabled = true
	world := testWorld()
	delete(world.Players, 1)
	encounter := &Encounter{ID: "enc-1", State: "active", Tags: []string{"pack"}}
	plans, err := EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[0].Status != "BLOCK_PLAYER_DATA_UNAVAILABLE" ||
		plans[0].ScheduledOffsetMS != nil || plans[1].ScheduledOffsetMS == nil ||
		*plans[1].ScheduledOffsetMS != 275 {
		t.Fatalf("P2 should be first scheduled candidate: %+v, %v", plans, err)
	}
}

func TestSafetyEvidenceBlocksPlans(t *testing.T) {
	profile := DefaultProfile()
	profile.Enabled = true
	world := testWorld()
	check := func(w World, want string) {
		t.Helper()
		plans, err := Evaluate(profile, w)
		if err != nil || plans[0].Status != want || plans[0].ComboID != "" {
			t.Fatalf("wanted %s, got %+v, %v", want, plans, err)
		}
	}
	world.SafeZone = ptr(true)
	check(world, "BLOCK_SAFE_ZONE")
	world.SafeZone = ptr(false)
	world.DataAgeMS = ptr(751)
	check(world, "BLOCK_WORLD_DATA_STALE")
	world.DataAgeMS = ptr(100)
	world.UI.ChatOpen = ptr(true)
	check(world, "BLOCK_UI_OPEN")
	world.UI.ChatOpen = ptr(false)
	world.Players[1] = Player{Alive: ptr(false), Loading: ptr(false), ActiveCombo: ptr(false), DistanceFromMainM: ptr(0.0)}
	check(world, "BLOCK_CHARACTER_DEAD")
	world.Players[1] = Player{Alive: ptr(true), Loading: ptr(true), ActiveCombo: ptr(false), DistanceFromMainM: ptr(0.0)}
	check(world, "BLOCK_LOADING")
	world.Players[1] = Player{Alive: ptr(true), Loading: ptr(false), ActiveCombo: ptr(false), DistanceFromMainM: ptr(0.0)}
	world.UI.AnyMenuOpen = nil
	if _, err := Evaluate(profile, world); err == nil {
		t.Fatal("missing UI menu evidence was treated as closed")
	}
	world.UI.AnyMenuOpen = ptr(false)
	world.Monsters = []Monster{}
	world.EmergencyActive = ptr(true)
	check(world, "waiting_no_encounter")
	world.Monsters = testWorld().Monsters[:1]
	world.Monsters[0].DistanceByPlayer[1] = 100
	check(world, "waiting_no_encounter")
	world.Monsters = testWorld().Monsters
	delete(world.Players, 1)
	check(world, "BLOCK_PLAYER_DATA_UNAVAILABLE")
	world = testWorld()
	world.Monsters[0].Rarity = "unknown"
	if _, err := Evaluate(profile, world); err == nil {
		t.Fatal("unknown monster rarity was guessed")
	}
}

func TestActiveComboUsesTargetExitRadiusAndSinglePackEnemy(t *testing.T) {
	profile := DefaultProfile()
	profile.Enabled = true
	world := testWorld()
	world.Monsters = world.Monsters[:1]
	world.Monsters[0].DistanceByPlayer[1] = 70
	world.Monsters[0].DistanceFromMainM = ptr(70.0)
	encounter := &Encounter{State: "active", Tags: []string{"pack"}}

	plans, err := EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[0].Status != "waiting_no_eligible_encounter" {
		t.Fatalf("inactive role should require three enemies within 60 m: %+v, %v", plans, err)
	}
	player := world.Players[1]
	player.ActiveCombo = ptr(true)
	world.Players[1] = player
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[0].ComboID != "primary-clear" || plans[0].Status != "planned_only" ||
		plans[1].Status != "waiting_no_eligible_encounter" {
		t.Fatalf("active role should retain a single enemy within 81 m: %+v, %v", plans, err)
	}
	world.Monsters[0].DistanceByPlayer[1] = 82
	plans, err = EvaluateEncounter(profile, world, encounter)
	if err != nil || plans[0].Status != "waiting_no_eligible_encounter" {
		t.Fatalf("active role should reject enemy beyond 81 m: %+v, %v", plans, err)
	}
	player.ActiveCombo = nil
	world.Players[1] = player
	if _, err := EvaluateEncounter(profile, world, encounter); err == nil {
		t.Fatal("missing active-combo evidence was accepted")
	}
}

func TestNoEncounterAndEndingPrecedeSafetyGate(t *testing.T) {
	profile := DefaultProfile()
	profile.Enabled = true
	world := testWorld()
	world.Monsters = []Monster{}
	world.SafeZone = ptr(true)
	world.UI.AnyMenuOpen = ptr(true)
	world.DataAgeMS = ptr(1000)
	plans, err := Evaluate(profile, world)
	if err != nil || plans[0].Status != "waiting_no_encounter" || plans[0].Reason != "In town" {
		t.Fatalf("no encounter in town should precede safety checks: %+v, %v", plans, err)
	}
	world.SafeZone = ptr(false)
	plans, err = Evaluate(profile, world)
	if err != nil || plans[0].Status != "waiting_no_encounter" ||
		plans[0].Reason != "Waiting: no enemies in range" {
		t.Fatalf("no encounter should use source idle reason: %+v, %v", plans, err)
	}
	ending := &Encounter{State: "winding-down"}
	plans, err = EvaluateEncounter(profile, world, ending)
	if err != nil || plans[0].Status != "waiting_fight_ending" || plans[0].ComboID != "" {
		t.Fatalf("ending encounter should precede safety checks: %+v, %v", plans, err)
	}
}
