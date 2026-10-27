package combat

import (
	"fmt"
)

// ActiveExecution is explicit replay evidence about a combo already running.
// It is not inferred from a planned combo or an observed button press.
type ActiveExecution struct {
	EncounterID string `json:"encounterId"`
	Behavior    string `json:"behavior"`
	ComboID     string `json:"comboId"`
	TargetID    string `json:"targetId,omitempty"`
}

type ExecutionPreview struct {
	Player            int    `json:"player"`
	Status            string `json:"status"`
	CurrentTargetID   string `json:"currentTargetId,omitempty"`
	ProposedTargetID  string `json:"proposedTargetId,omitempty"`
	EffectiveTargetID string `json:"effectiveTargetId,omitempty"`
}

// PreviewExecutions compares explicit active executions with new read-only
// plans. It models only the target-lock and in-place retarget branches in the
// supplied runtime; other execution gates are left unresolved.
func PreviewExecutions(profile Profile, world World, encounter *Encounter,
	plans []Plan) ([]ExecutionPreview, error) {
	if len(world.ActiveExecutions) == 0 {
		return nil, nil
	}
	combos := make(map[string]Combo, len(profile.Combos))
	for _, combo := range profile.Combos {
		combos[combo.ID] = combo
	}
	planByPlayer := make(map[int]Plan, len(plans))
	for _, plan := range plans {
		planByPlayer[plan.Player] = plan
	}
	preview := make([]ExecutionPreview, 0, len(world.ActiveExecutions))
	for _, role := range profile.Roles {
		active, exists := world.ActiveExecutions[role.Player]
		if !exists {
			continue
		}
		player, hasPlayer := world.Players[role.Player]
		combo, hasCombo := combos[active.ComboID]
		if !hasPlayer || player.ActiveCombo == nil || !*player.ActiveCombo || !hasCombo {
			return nil, fmt.Errorf("P%d active execution has no verified running combo", role.Player)
		}
		plan, hasPlan := planByPlayer[role.Player]
		entry := ExecutionPreview{Player: role.Player, CurrentTargetID: active.TargetID}
		if !hasPlan || plan.Status != "planned_only" || encounter == nil {
			entry.Status = "no_planned_decision"
			preview = append(preview, entry)
			continue
		}
		entry.ProposedTargetID = plan.TargetID
		if role.Logic.TargetLock != "free" && plan.Behavior != "emergency" &&
			plan.Behavior == active.Behavior && plan.ComboID == active.ComboID &&
			active.TargetID != "" && active.TargetID != plan.TargetID &&
			hasLiveMonster(world.Monsters, active.TargetID) {
			entry.Status = "hold_locked_target"
			entry.EffectiveTargetID = active.TargetID
		} else if active.EncounterID == encounter.ID && active.ComboID == plan.ComboID &&
			combo.Repeat != "once-per-target" && !requiresSpecificTarget(combo) {
			entry.Status = "retarget_in_place"
			entry.EffectiveTargetID = plan.TargetID
		} else if active.EncounterID == encounter.ID && active.ComboID == plan.ComboID &&
			active.TargetID == plan.TargetID {
			entry.Status = "same_decision"
			entry.EffectiveTargetID = active.TargetID
		} else {
			entry.Status = "other_runtime_gate_pending"
		}
		preview = append(preview, entry)
	}
	if len(preview) != len(world.ActiveExecutions) {
		return nil, fmt.Errorf("active execution refers to a player outside the profile")
	}
	return preview, nil
}

func hasLiveMonster(monsters []Monster, id string) bool {
	for _, monster := range monsters {
		if monster.ID == id && monster.Alive != nil && *monster.Alive {
			return true
		}
	}
	return false
}
