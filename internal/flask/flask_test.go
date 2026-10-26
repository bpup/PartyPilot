package flask

import (
	"testing"
	"time"
)

func vitals(life, mana float64) Vitals {
	return Vitals{Life: Pool{number(life), number(100)}, Mana: Pool{number(mana), number(100)}}
}

func TestResetProfileAndEdges(t *testing.T) {
	p1, err := ResetProfiles(1)
	if err != nil || p1.Life.Trigger != 70 || p1.Mana.Trigger != 45 || *p1.Life.Emergency != 25 {
		t.Fatalf("P1 reset profile: %+v %v", p1, err)
	}
	p2, err := ResetProfiles(2)
	if err != nil || p2.Life.Trigger != 45 || p2.Life.Cooldown != 400*time.Millisecond || p2.Life.MinMissing != 25 {
		t.Fatalf("P2 reset profile: %+v %v", p2, err)
	}
	p2.Mana.Enabled = false
	engine := New()
	now := time.Unix(100, 0)
	step := func(after time.Duration, life float64, menu, recovering bool) []Activation {
		t.Helper()
		result, err := engine.Step(2, now.Add(after), vitals(life, 100), p2, menu, recovering)
		if err != nil {
			t.Fatal(err)
		}
		return result
	}
	if got := step(0, 30, true, false); len(got) != 0 {
		t.Fatalf("menu allowed activation: %+v", got)
	}
	if got := step(100*time.Millisecond, 30, false, true); len(got) != 0 {
		t.Fatalf("recovery allowed activation: %+v", got)
	}
	if got := step(200*time.Millisecond, 30, false, false); len(got) != 1 || got[0].Reason != "threshold" {
		t.Fatalf("first threshold missed: %+v", got)
	}
	if got := step(600*time.Millisecond, 30, false, false); len(got) != 0 {
		t.Fatalf("low life repeated without sustained use: %+v", got)
	}
	if got := step(650*time.Millisecond, 15, false, false); len(got) != 1 || got[0].Reason != "emergency" {
		t.Fatalf("emergency edge missed: %+v", got)
	}
	if got := step(700*time.Millisecond, 15, false, false); len(got) != 0 {
		t.Fatalf("emergency edge repeated: %+v", got)
	}
	step(750*time.Millisecond, 80, false, false)
	if got := step(800*time.Millisecond, 15, false, false); len(got) != 1 || got[0].Reason != "emergency" {
		t.Fatalf("rearmed emergency edge missed: %+v", got)
	}
}

func TestSustainedCooldownAndMissingVitals(t *testing.T) {
	rules, _ := ResetProfiles(1)
	rules.Mana.Enabled = false
	rules.Life.Sustained = true
	engine := New()
	now := time.Unix(100, 0)
	first, err := engine.Step(1, now, vitals(50, 100), rules, false, false)
	if err != nil || len(first) != 1 {
		t.Fatalf("first activation: %+v %v", first, err)
	}
	missing := Vitals{Mana: Pool{number(100), number(100)}}
	if got, err := engine.Step(1, now.Add(100*time.Millisecond), missing, rules, false, false); err != nil || len(got) != 0 {
		t.Fatalf("missing life generated activation: %+v %v", got, err)
	}
	if got, err := engine.Step(1, now.Add(150*time.Millisecond), vitals(50, 100), rules, false, false); err != nil || len(got) != 0 {
		t.Fatalf("cooldown not enforced: %+v %v", got, err)
	}
	if got, err := engine.Step(1, now.Add(200*time.Millisecond), vitals(50, 100), rules, false, false); err != nil || len(got) != 1 || got[0].Reason != "sustained" {
		t.Fatalf("sustained retry missing: %+v %v", got, err)
	}
}

func TestCompanionMinimumDeficitAndSeparatePlayerState(t *testing.T) {
	rules, _ := ResetProfiles(2)
	rules.Mana.Enabled = false
	engine := New()
	now := time.Unix(100, 0)
	smallPool := Vitals{Life: Pool{number(5), number(20)}}
	if got, err := engine.Step(2, now, smallPool, rules, false, false); err != nil || len(got) != 0 {
		t.Fatalf("minimum missing life ignored: %+v %v", got, err)
	}
	if got, err := engine.Step(2, now.Add(time.Millisecond), vitals(30, 100), rules, false, false); err != nil || len(got) != 1 {
		t.Fatalf("P2 did not fire after sufficient deficit: %+v %v", got, err)
	}
	if got, err := engine.Step(4, now.Add(time.Millisecond), vitals(30, 100), rules, false, false); err != nil || len(got) != 1 {
		t.Fatalf("P4 inherited P2 cooldown state: %+v %v", got, err)
	}
	if _, err := engine.Step(2, now, vitals(30, 100), rules, false, false); err == nil {
		t.Fatal("out-of-order frame accepted")
	}
}

func TestEnergyShieldCanReplaceOrSupplementLife(t *testing.T) {
	rules, _ := ResetProfiles(1)
	rules.Mana.Enabled = false
	now := time.Unix(100, 0)
	ci := Vitals{Life: Pool{number(1), number(1)}, EnergyShield: Pool{number(20), number(100)}}
	if got, err := New().Step(1, now, ci, rules, false, false); err != nil || len(got) != 1 || got[0].Percent != 20 {
		t.Fatalf("CI did not use energy shield: %+v %v", got, err)
	}
	combined := Vitals{Life: Pool{number(100), number(100)}, EnergyShield: Pool{number(0), number(100)}}
	if got, err := New().Step(1, now, combined, rules, false, false); err != nil || len(got) != 0 {
		t.Fatalf("ES supplemented life without rule: %+v %v", got, err)
	}
	rules.CombinedDefence = true
	if got, err := New().Step(1, now, combined, rules, false, false); err != nil || len(got) != 1 || got[0].Percent != 50 {
		t.Fatalf("combined defence did not use both pools: %+v %v", got, err)
	}
}

func TestWardDefersLifeAndBreakForcesOneDecision(t *testing.T) {
	rules, _ := ResetProfiles(1)
	rules.Mana.Enabled = false
	engine := New()
	now := time.Unix(100, 0)
	withWard := vitals(20, 100)
	withWard.Ward = Pool{Current: number(10)}
	if got, err := engine.Step(1, now, withWard, rules, false, false); err != nil || len(got) != 0 {
		t.Fatalf("active ward did not defer life: %+v %v", got, err)
	}
	broken := vitals(20, 100)
	broken.Ward = Pool{Current: number(0)}
	if got, err := engine.Step(1, now.Add(100*time.Millisecond), broken, rules, false, false); err != nil || len(got) != 1 || got[0].Reason != "ward_break" {
		t.Fatalf("ward break did not fire: %+v %v", got, err)
	}
	if got, err := engine.Step(1, now.Add(200*time.Millisecond), broken, rules, false, false); err != nil || len(got) != 0 {
		t.Fatalf("ward break repeated: %+v %v", got, err)
	}
	unknown := vitals(100, 100)
	engine.Step(2, now, withWard, rules, false, false)
	engine.Step(2, now.Add(100*time.Millisecond), unknown, rules, false, false)
	if got, err := engine.Step(2, now.Add(200*time.Millisecond), broken, rules, false, false); err != nil || len(got) != 1 || got[0].Reason == "ward_break" {
		t.Fatalf("missing ward was treated as a break: %+v %v", got, err)
	}
}
