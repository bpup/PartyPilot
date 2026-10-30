package live

import (
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/flask"
	"partypilot/internal/follow"
	"partypilot/internal/telemetry"
)

func TestLiveDrinkerSupportNeedsBothBoundActorsInSameArea(t *testing.T) {
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
	binding := FlaskSupportBinding{DrinkerPlayer: 2, SupportBuild: true,
		Monitor: flask.SupportMonitor{Enabled: true, TargetPlayer: 1,
			MonitorResource: "Life", TriggerPercent: 50, FlaskResource: "Life"}}
	if err := session.ConfigureSupport([]FlaskSupportBinding{binding}); err != nil {
		t.Fatal(err)
	}
	if err := session.ConfigureSupport([]FlaskSupportBinding{binding, binding}); err == nil {
		t.Fatal("duplicate support bindings were accepted")
	}
	if err := session.SetMenuGuard(2, "clear"); err != nil {
		t.Fatal(err)
	}
	start := time.Unix(100, 0)
	for _, slot := range []int{1, 2} {
		liveEngine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: start, Origin: "live"})
	}
	num := func(value float64) *float64 { return &value }
	read := func(pid int, after time.Duration, life float64) []flask.Activation {
		t.Helper()
		at := start.Add(after)
		sample := telemetry.Sample{ProcessID: pid, SeenUTC: at, ReceivedAt: at,
			TickMS: at.UnixMilli(), CoordinateSpace: "world", AreaHash: 55,
			Life: num(life), LifeMax: num(100), Mana: num(100), ManaMax: num(100)}
		if !liveEngine.ObserveSample(sample) {
			t.Fatal("valid bound motion was rejected")
		}
		activations, err := session.ObserveSample(sample, at)
		if err != nil {
			t.Fatal(err)
		}
		return activations
	}
	if got := read(101, time.Millisecond, 40); len(got) != 0 {
		t.Fatalf("target alone caused support: %+v", got)
	}
	if got := read(202, 2*time.Millisecond, 100); len(got) != 1 ||
		got[0].Player != 2 || got[0].Resource != "life" || got[0].Reason != "support" {
		t.Fatalf("bound target did not cause drinker support: %+v", got)
	}
	if got := read(101, 3*time.Millisecond, 40); len(got) != 0 {
		t.Fatalf("one low episode repeated support: %+v", got)
	}
	read(101, 4*time.Millisecond, 100) // The watched player recovers above the threshold.
	if got := read(101, 500*time.Millisecond, 40); len(got) != 1 ||
		got[0].Player != 2 || got[0].Reason != "support" {
		t.Fatalf("support did not rearm after recovery and cooldown: %+v", got)
	}
	liveEngine.ObserveArea(arealog.Signal{Slot: 1, AreaID: "G2_5", Seed: 456,
		ObservedAt: start.Add(501 * time.Millisecond), Origin: "live"})
	if got := read(101, 502*time.Millisecond, 40); len(got) != 0 {
		t.Fatalf("different area instance caused support: %+v", got)
	}
}

func TestLiveDrinkerOptionsMustAgreeAcrossTargets(t *testing.T) {
	liveEngine, err := New(map[int]int{1: 101, 2: 202, 3: 303}, 1,
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
	first := FlaskSupportBinding{DrinkerPlayer: 2, SupportBuild: true,
		Monitor: flask.SupportMonitor{TargetPlayer: 1, MonitorResource: "Life",
			TriggerPercent: 50, FlaskResource: "Life"}}
	second := first
	second.Monitor.TargetPlayer = 3
	second.BuildHasNoMana = true
	if err := session.ConfigureSupport([]FlaskSupportBinding{first, second}); err == nil ||
		err.Error() != "inconsistent drinker flask settings" {
		t.Fatalf("inconsistent drinker settings: %v", err)
	}
	first.BuildHasNoMana, second.BuildHasNoMana = true, true
	first.SustainedUse, second.SustainedUse = true, true
	if err := session.ConfigureSupport([]FlaskSupportBinding{first, second}); err != nil {
		t.Fatal(err)
	}
	rules, err := session.rulesFor(2)
	if err != nil || !rules.BuildHasNoMana || !rules.SustainedUse || !rules.SupportBuild {
		t.Fatalf("drinker options did not reach own rules: %+v %v", rules, err)
	}
}

func TestLiveNoManaDrinkerCanUseOwnLifeFlask(t *testing.T) {
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
	if err := session.ConfigureSupport([]FlaskSupportBinding{{DrinkerPlayer: 2,
		BuildHasNoMana: true, Monitor: flask.SupportMonitor{TargetPlayer: 1,
			MonitorResource: "Life", TriggerPercent: 50, FlaskResource: "Life"}}}); err != nil {
		t.Fatal(err)
	}
	if err := session.SetMenuGuard(2, "clear"); err != nil {
		t.Fatal(err)
	}
	start := time.Unix(300, 0)
	liveEngine.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_4", Seed: 123,
		ObservedAt: start, Origin: "live"})
	num := func(value float64) *float64 { return &value }
	sample := telemetry.Sample{ProcessID: 202, SeenUTC: start.Add(time.Millisecond),
		ReceivedAt: start.Add(time.Millisecond), TickMS: start.Add(time.Millisecond).UnixMilli(),
		CoordinateSpace: "world", AreaHash: 55, Life: num(30), LifeMax: num(100),
		Mana: num(0), ManaMax: num(0)}
	if !liveEngine.ObserveSample(sample) {
		t.Fatal("zero-mana resource sample was rejected")
	}
	activations, err := session.ObserveSample(sample, sample.SeenUTC)
	if err != nil || len(activations) != 1 || activations[0].Player != 2 ||
		activations[0].Resource != "life" {
		t.Fatalf("no-mana drinker did not use own life flask: %+v %v", activations, err)
	}
}
