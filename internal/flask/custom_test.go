package flask

import (
	"strings"
	"testing"
	"time"
)

func customRule() CustomRule {
	return CustomRule{ID: "recovery-x", Enabled: true, Player: "P1", Resource: "Energy Shield",
		Comparator: "≤", Threshold: 50, CooldownSeconds: 1, Priority: 50,
		MinDeficit: 0, Dependency: "None", CombatOnly: false, Input: "X"}
}

func TestCustomRecoveryCoexistsWithLifeFlaskAndUsesOwnCooldown(t *testing.T) {
	engine := New()
	rules, _ := ResetProfiles(1)
	rules.Mana.Enabled = false
	vit := Vitals{Life: Pool{number(30), number(100)},
		EnergyShield: Pool{number(300), number(1000)}}
	now := time.Unix(100, 0).UTC()
	custom := []CustomRule{customRule()}
	first, err := engine.StepWithCustom(1, now, vit, rules, false, false, custom, true)
	if err != nil || len(first) != 2 || first[0].Resource != "life" ||
		first[1].Resource != "action" || first[1].DirectInput != "X" ||
		!strings.Contains(first[1].Reason, "priority 50") {
		t.Fatalf("target life and custom X decisions did not coexist: %+v, %v", first, err)
	}
	blocked, err := engine.StepWithCustom(1, now.Add(500*time.Millisecond), vit, rules,
		false, false, custom, true)
	if err != nil || len(blocked) != 0 {
		t.Fatalf("custom cooldown did not hold independently: %+v, %v", blocked, err)
	}
	rearmed, err := engine.StepWithCustom(1, now.Add(time.Second), vit, rules,
		false, false, custom, true)
	if err != nil || len(rearmed) != 1 || rearmed[0].DirectInput != "X" {
		t.Fatalf("custom X did not rearm on its own cooldown: %+v, %v", rearmed, err)
	}
}

func TestCustomRecoveryTargetComparatorAndCombatGuard(t *testing.T) {
	engine := New()
	rules, _ := ResetProfiles(1)
	rules.Life.Enabled, rules.Mana.Enabled = false, false
	vit := Vitals{Life: Pool{number(100), number(100)},
		EnergyShield: Pool{number(700), number(1000)}}
	now := time.Unix(100, 0).UTC()
	rule := customRule()
	rule.Player, rule.Comparator, rule.Threshold, rule.CombatOnly = "Both", "≥", 70, true
	rule.Input = "D-pad Left"
	for _, candidate := range []struct {
		player   int
		combat   bool
		menuOpen bool
		want     bool
	}{
		{1, true, false, true}, {2, false, false, false},
		{3, true, false, false}, {2, true, true, false},
	} {
		got, err := engine.StepWithCustom(candidate.player, now, vit, rules,
			candidate.menuOpen, false, []CustomRule{rule}, candidate.combat)
		if err != nil || (len(got) == 1) != candidate.want {
			t.Fatalf("player %d combat %v menu %v: %+v, %v", candidate.player,
				candidate.combat, candidate.menuOpen, got, err)
		}
		if candidate.want && (got[0].Resource != "life" || got[0].DirectInput != "") {
			t.Fatalf("D-pad Left did not retain life-flask behavior: %+v", got)
		}
	}
}

func TestCustomRecoveryWardBreakDependency(t *testing.T) {
	engine := New()
	rules, _ := ResetProfiles(1)
	rules.Life.Enabled, rules.Mana.Enabled, rules.FireLifeOnWardBreak = false, false, false
	rule := customRule()
	rule.Resource, rule.Dependency, rule.Input = "Life", "Ward broken", "D-pad Right"
	vit := Vitals{Life: Pool{number(20), number(100)}, Ward: Pool{number(50), number(100)}}
	now := time.Unix(100, 0).UTC()
	first, err := engine.StepWithCustom(1, now, vit, rules, false, false, []CustomRule{rule}, true)
	if err != nil || len(first) != 0 {
		t.Fatalf("ward break fired before break: %+v, %v", first, err)
	}
	vit.Ward.Current = number(0)
	second, err := engine.StepWithCustom(1, now.Add(100*time.Millisecond), vit, rules,
		false, false, []CustomRule{rule}, true)
	if err != nil || len(second) != 1 || second[0].Resource != "mana" {
		t.Fatalf("ward break did not fire D-pad Right: %+v, %v", second, err)
	}
}

func TestCustomRecoveryRejectsUnverifiedInputs(t *testing.T) {
	rules := []CustomRule{customRule()}
	for _, changed := range []func(*CustomRule){
		func(rule *CustomRule) { rule.Input = "A" },
		func(rule *CustomRule) { rule.Resource = "Rage" },
		func(rule *CustomRule) { rule.CooldownSeconds = 0 },
		func(rule *CustomRule) { rule.Player = "Anyone" },
	} {
		copyRules := append([]CustomRule(nil), rules...)
		changed(&copyRules[0])
		if err := validateCustomRules(copyRules); err == nil {
			t.Fatalf("unsupported custom recovery setting accepted: %+v", copyRules[0])
		}
	}
}
