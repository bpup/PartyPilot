package combat

import "testing"

func TestExecutionPreviewKeepsLockedLiveTarget(t *testing.T) {
	profile := DefaultProfile()
	profile.Roles[0].Logic.TargetLock = "until-dead"
	world := testWorld()
	player := world.Players[1]
	player.ActiveCombo = ptr(true)
	world.Players[1] = player
	world.ActiveExecutions = map[int]ActiveExecution{1: {
		EncounterID: "enc-1", Behavior: "pack", ComboID: "primary-clear", TargetID: "a"}}
	encounter := &Encounter{ID: "enc-1", State: "active"}
	plans := []Plan{{Player: 1, Status: "planned_only", Behavior: "pack",
		ComboID: "primary-clear", TargetID: "c"}}
	preview, err := PreviewExecutions(profile, world, encounter, plans)
	if err != nil || len(preview) != 1 || preview[0].Status != "hold_locked_target" ||
		preview[0].CurrentTargetID != "a" || preview[0].ProposedTargetID != "c" ||
		preview[0].EffectiveTargetID != "a" || plans[0].TargetID != "c" {
		t.Fatalf("live locked target did not stay separate from the next plan: %+v, %v", preview, err)
	}
	profile.Roles[0].Logic.TargetLock = "when-invalid"
	preview, err = PreviewExecutions(profile, world, encounter, plans)
	if err != nil || preview[0].Status != "hold_locked_target" {
		t.Fatalf("when-invalid lock differed from supplied runtime branch: %+v, %v", preview, err)
	}
	profile.Roles[0].Logic.TargetLock = "free"
	preview, err = PreviewExecutions(profile, world, encounter, plans)
	if err != nil || preview[0].Status != "retarget_in_place" ||
		preview[0].EffectiveTargetID != "c" {
		t.Fatalf("free target did not retarget a repeatable combo: %+v, %v", preview, err)
	}
	profile.Roles[0].Logic.TargetLock = "until-dead"
	world.Monsters[0].Alive = ptr(false)
	preview, err = PreviewExecutions(profile, world, encounter, plans)
	if err != nil || preview[0].Status != "retarget_in_place" {
		t.Fatalf("dead locked target was retained: %+v, %v", preview, err)
	}
}

func TestExecutionPreviewDoesNotClaimUnmodelledRuntimeGate(t *testing.T) {
	profile := DefaultProfile()
	profile.Roles[0].Logic.TargetLock = "until-dead"
	world := testWorld()
	player := world.Players[1]
	player.ActiveCombo = ptr(true)
	world.Players[1] = player
	world.ActiveExecutions = map[int]ActiveExecution{1: {
		EncounterID: "enc-1", Behavior: "boss", ComboID: "boss-engage", TargetID: "a"}}
	encounter := &Encounter{ID: "enc-1", State: "active"}
	plans := []Plan{{Player: 1, Status: "planned_only", Behavior: "emergency",
		ComboID: "emergency-defence", TargetID: "c"}}
	preview, err := PreviewExecutions(profile, world, encounter, plans)
	if err != nil || preview[0].Status != "other_runtime_gate_pending" ||
		preview[0].EffectiveTargetID != "" {
		t.Fatalf("emergency was incorrectly treated as a locked target: %+v, %v", preview, err)
	}
	plans[0].Behavior, plans[0].ComboID = "boss", "boss-engage"
	preview, err = PreviewExecutions(profile, world, encounter, plans)
	if err != nil || preview[0].Status != "hold_locked_target" {
		t.Fatalf("same-slot boss lock was lost: %+v, %v", preview, err)
	}
	profile.Roles[0].Logic.TargetLock = "free"
	preview, err = PreviewExecutions(profile, world, encounter, plans)
	if err != nil || preview[0].Status != "other_runtime_gate_pending" {
		t.Fatalf("once-per-target combo was incorrectly retargeted: %+v, %v", preview, err)
	}
	player.ActiveCombo = ptr(false)
	world.Players[1] = player
	if _, err := PreviewExecutions(profile, world, encounter, plans); err == nil {
		t.Fatal("active execution without running-combo evidence was accepted")
	}
}
