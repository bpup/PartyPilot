package flask

import (
	"testing"
	"time"
)

func supportRules() Settings {
	rules, _ := ResetProfiles(2)
	rules.SupportBuild = true
	return rules
}

func TestSupportBothSharesCooldownAndRearms(t *testing.T) {
	engine := New()
	rules := supportRules()
	monitor := SupportMonitor{Enabled: true, TargetPlayer: 1,
		MonitorResource: "Life", TriggerPercent: 50, FlaskResource: "Both"}
	start := time.Unix(100, 0)
	self, err := engine.Step(2, start, vitals(100, 10), rules, false, false)
	if err != nil || len(self) != 1 || self[0].Resource != "mana" {
		t.Fatalf("self mana cooldown was not established: %+v %v", self, err)
	}
	low := vitals(40, 100)
	first, err := engine.StepSupport(2, start, low, rules, monitor, false)
	if err != nil || len(first) != 1 || first[0].Resource != "life" || first[0].Player != 2 ||
		first[0].Reason != "Drinker support · P1 life ≤ 50%" {
		t.Fatalf("support ignored independent life cooldown: %+v %v", first, err)
	}
	second, err := engine.StepSupport(2, start.Add(800*time.Millisecond), low, rules, monitor, false)
	if err != nil || len(second) != 1 || second[0].Resource != "mana" {
		t.Fatalf("support did not use the mana flask after its cooldown: %+v %v", second, err)
	}
	if got, _ := engine.StepSupport(2, start.Add(time.Second), low, rules, monitor, false); len(got) != 0 {
		t.Fatalf("one low episode repeated support input: %+v", got)
	}
	engine.StepSupport(2, start.Add(1100*time.Millisecond), vitals(100, 100), rules, monitor, false)
	if got, err := engine.StepSupport(2, start.Add(1600*time.Millisecond), low, rules, monitor, false); err != nil || len(got) != 2 {
		t.Fatalf("watched recovery did not rearm both flasks: %+v %v", got, err)
	}
}

func TestSupportWatchChoicesAndMissingData(t *testing.T) {
	data := vitals(40, 90)
	data.EnergyShield = Pool{Current: number(0), Maximum: number(0)}
	for _, test := range []struct {
		watch string
		fires bool
	}{
		{"Life", true}, {"Mana", false}, {"Energy Shield", false},
		{"Life AND Mana", false}, {"Life OR Mana", true},
		{"Any: Life / Mana / ES", true},
	} {
		rules := supportRules()
		monitor := SupportMonitor{Enabled: true, TargetPlayer: 1,
			MonitorResource: test.watch, TriggerPercent: 50, FlaskResource: "Life"}
		got, err := New().StepSupport(2, time.Unix(100, 0), data, rules, monitor, false)
		if err != nil || (len(got) == 1) != test.fires {
			t.Fatalf("watch %q: %+v %v", test.watch, got, err)
		}
	}
	missing := Vitals{Life: Pool{Current: number(90), Maximum: number(100)}}
	monitor := SupportMonitor{Enabled: true, TargetPlayer: 1,
		MonitorResource: "Life OR Mana", TriggerPercent: 50, FlaskResource: "Life"}
	if got, _ := New().StepSupport(2, time.Unix(100, 0), missing, supportRules(), monitor, false); len(got) != 0 {
		t.Fatalf("missing mana made high life appear low: %+v", got)
	}
	monitor.QuickTopUp = true
	if got, _ := New().StepSupport(2, time.Unix(100, 0), missing, supportRules(), monitor, false); len(got) != 1 {
		t.Fatalf("quick top-up did not use 99 percent any-resource watch: %+v", got)
	}
}

func TestSupportSafetyAndReset(t *testing.T) {
	engine := New()
	rules := supportRules()
	monitor := SupportMonitor{Enabled: true, TargetPlayer: 1,
		MonitorResource: "Life", TriggerPercent: 50, FlaskResource: "Both"}
	start := time.Unix(100, 0)
	low := vitals(40, 100)
	if got, _ := engine.StepSupport(2, start, low, rules, monitor, true); len(got) != 0 {
		t.Fatalf("menu guard allowed support: %+v", got)
	}
	rules.BuildHasNoMana = true
	if got, _ := engine.StepSupport(2, start, low, rules, monitor, false); len(got) != 1 || got[0].Resource != "life" {
		t.Fatalf("no-mana build fired a mana flask: %+v", got)
	}
	engine.ResetPlayer(1)
	if got, _ := engine.StepSupport(2, start.Add(400*time.Millisecond), low, rules, monitor, false); len(got) != 1 {
		t.Fatalf("target reset retained support latch: %+v", got)
	}
	rules.SupportBuild = false
	if got, _ := engine.StepSupport(2, start.Add(time.Second), low, rules, monitor, false); len(got) != 0 {
		t.Fatalf("disabled support build fired: %+v", got)
	}
	monitor.TargetPlayer = 2
	if got, _ := engine.StepSupport(2, start.Add(2*time.Second), low, supportRules(), monitor, false); len(got) != 0 {
		t.Fatalf("drinker targeted itself: %+v", got)
	}
}
