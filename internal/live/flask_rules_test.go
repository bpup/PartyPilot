package live

import (
	"testing"
	"time"

	"partypilot/internal/flask"
	"partypilot/internal/follow"
)

func TestConfiguredFlaskRulesStayBoundToEachPlayer(t *testing.T) {
	liveEngine, err := New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10,
			MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer liveEngine.Close()
	session, err := NewFlaskSession(liveEngine, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	first := FlaskRuleBinding{Player: 1, LifeEnabled: true, LifeTrigger: 30,
		LifeCooldownMS: 500, Emergency: 25, LifeMinMissing: 10,
		SkipRecovering: true, ManaEnabled: false, ManaTrigger: 45,
		ManaCooldownMS: 800, ManaMinMissing: 0, ChaosInoculation: true}
	second := first
	second.Player, second.LifeTrigger = 2, 80
	second.ChaosInoculation, second.CombinedDefence = false, true
	if err := session.ConfigureRules([]FlaskRuleBinding{first}); err == nil {
		t.Fatal("partial player rule file was accepted")
	}
	if err := session.ConfigureRules([]FlaskRuleBinding{first, first}); err == nil {
		t.Fatal("duplicate player rule file was accepted")
	}
	invalid := second
	invalid.LifeCooldownMS = 99
	if err := session.ConfigureRules([]FlaskRuleBinding{first, invalid}); err == nil {
		t.Fatal("cooldown outside the target control range was accepted")
	}
	if err := session.ConfigureRules([]FlaskRuleBinding{first, second}); err != nil {
		t.Fatal(err)
	}
	p1, err := session.rulesFor(1)
	if err != nil {
		t.Fatal(err)
	}
	p2, err := session.rulesFor(2)
	if err != nil || p1.Life.Trigger != 30 || p2.Life.Trigger != 80 ||
		p1.Life.Cooldown != 500*time.Millisecond || p2.Mana.Enabled ||
		p1.Life.MinMissing != 10 || !p1.Life.SkipRecovering ||
		!p1.ChaosInoculation || p1.CombinedDefence ||
		p2.ChaosInoculation || !p2.CombinedDefence {
		t.Fatalf("player flask rules were not isolated: P1=%+v P2=%+v %v", p1, p2, err)
	}
	data := flask.Vitals{Life: flask.Pool{Current: ptr(50), Maximum: ptr(100)}}
	if got, err := flask.New().Step(1, time.Unix(100, 0), data, p1, false, false); err != nil || len(got) != 0 {
		t.Fatalf("P1 fired above its configured threshold: %+v %v", got, err)
	}
	if got, err := flask.New().Step(2, time.Unix(100, 0), data, p2, false, false); err != nil || len(got) != 1 || got[0].Resource != "life" {
		t.Fatalf("P2 missed its configured threshold: %+v %v", got, err)
	}
	ci := flask.Vitals{Life: flask.Pool{Current: ptr(1), Maximum: ptr(1)},
		EnergyShield: flask.Pool{Current: ptr(20), Maximum: ptr(100)}}
	if got, err := flask.New().Step(1, time.Unix(101, 0), ci, p1, false, false); err != nil || len(got) != 1 || got[0].Percent != 20 {
		t.Fatalf("configured one-life build did not use energy shield: %+v %v", got, err)
	}
	combined := flask.Vitals{Life: flask.Pool{Current: ptr(100), Maximum: ptr(100)},
		EnergyShield: flask.Pool{Current: ptr(0), Maximum: ptr(100)}}
	if got, err := flask.New().Step(2, time.Unix(101, 0), combined, p2, false, false); err != nil || len(got) != 1 || got[0].Percent != 50 {
		t.Fatalf("configured combined defence did not use both pools: %+v %v", got, err)
	}
}

func ptr(value float64) *float64 { return &value }
