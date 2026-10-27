package live

import (
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/flask"
	"partypilot/internal/follow"
	"partypilot/internal/telemetry"
)

func TestLiveFlasksRequireBoundFreshVitalsAndClearMenu(t *testing.T) {
	liveEngine, err := New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10,
			MaxAge: 750 * time.Millisecond}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer liveEngine.Close()
	session, err := NewFlaskSession(liveEngine, 750*time.Millisecond)
	if err != nil {
		t.Fatal(err)
	}
	start := time.Unix(100, 0)
	number := func(value float64) *float64 { return &value }
	sample := func(pid int, at time.Time, life float64) telemetry.Sample {
		return telemetry.Sample{ProcessID: pid, SeenUTC: at,
			ReceivedAt: at, TickMS: at.UnixMilli(), CoordinateSpace: "world", AreaHash: 55,
			Life: number(life), LifeMax: number(100), Mana: number(100), ManaMax: number(100)}
	}
	check := func(item telemetry.Sample, now time.Time, expected int) {
		t.Helper()
		liveEngine.ObserveSample(item)
		got, err := session.ObserveSample(item, now)
		if err != nil || len(got) != expected {
			t.Fatalf("flask count %d, expected %d: %+v %v", len(got), expected, got, err)
		}
	}
	for _, slot := range []int{1, 2} {
		liveEngine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: start, Origin: "live"})
	}
	check(sample(101, start, 30), start, 0) // Unknown menu state stays held.
	if err := session.SetMenuGuard(1, "clear"); err != nil {
		t.Fatal(err)
	}
	check(sample(999, start.Add(time.Millisecond), 30), start.Add(time.Millisecond), 0)
	check(sample(101, start.Add(2*time.Millisecond), 0), start.Add(2*time.Millisecond), 0)
	check(sample(101, start.Add(3*time.Millisecond), 30), start.Add(time.Second), 0)
	check(sample(101, start.Add(time.Second), 30), start.Add(time.Second), 1)
	check(sample(101, start.Add(time.Second), 30), start.Add(time.Second), 0)
	if err := session.SetMenuGuard(1, "menu_detected"); err != nil {
		t.Fatal(err)
	}
	check(sample(101, start.Add(2*time.Second), 80), start.Add(2*time.Second), 0)
	check(sample(101, start.Add(3*time.Second), 30), start.Add(3*time.Second), 0)
	if err := session.SetMenuGuard(1, "clear"); err != nil {
		t.Fatal(err)
	}
	check(sample(101, start.Add(4*time.Second), 30), start.Add(4*time.Second), 0)
	if err := session.SetMenuGuard(2, "clear"); err != nil {
		t.Fatal(err)
	}
	check(sample(202, start.Add(5*time.Second), 30), start.Add(5*time.Second), 1)
	if err := session.SetMenuGuard(3, "clear"); err == nil {
		t.Fatal("unbound player accepted a menu state")
	}
}

func TestCustomRecoveryNeedsBoundRecentAttack(t *testing.T) {
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
	rule := flask.CustomRule{ID: "es-recovery-x", Enabled: true, Player: "P1",
		Resource: "Energy Shield", Comparator: "≤", Threshold: 50,
		CooldownSeconds: 1, Priority: 50, MinDeficit: 0, Dependency: "None",
		CombatOnly: true, Input: "X"}
	if err := session.ConfigureCustomRules([]flask.CustomRule{rule}); err != nil {
		t.Fatal(err)
	}
	if err := session.SetMenuGuard(1, "clear"); err != nil {
		t.Fatal(err)
	}
	start := time.Unix(100, 0)
	liveEngine.ObserveArea(arealog.Signal{Slot: 1, AreaID: "G2_4", Seed: 123,
		ObservedAt: start, Origin: "live"})
	read := func(at time.Time) []flask.Activation {
		t.Helper()
		sample := telemetry.Sample{ProcessID: 101, SeenUTC: at, ReceivedAt: at,
			TickMS: at.UnixMilli(), CoordinateSpace: "world", AreaHash: 55,
			Life: ptr(100), LifeMax: ptr(100), EnergyShield: ptr(30), EnergyShieldMax: ptr(100)}
		if !liveEngine.ObserveSample(sample) {
			t.Fatal("custom recovery sample was rejected")
		}
		got, err := session.ObserveSample(sample, at)
		if err != nil {
			t.Fatal(err)
		}
		return got
	}
	if got := read(start); len(got) != 0 {
		t.Fatalf("combat-only rule fired without attack evidence: %+v", got)
	}
	if err := session.ObserveMainAttack(2, start.Add(time.Second), true); err != nil {
		t.Fatal(err)
	}
	if got := read(start.Add(time.Second)); len(got) != 0 {
		t.Fatalf("another player's attack armed P1: %+v", got)
	}
	if err := session.ObserveMainAttack(1, start.Add(2*time.Second), true); err != nil {
		t.Fatal(err)
	}
	if got := read(start.Add(2 * time.Second)); len(got) != 1 ||
		got[0].Resource != "action" || got[0].DirectInput != "X" {
		t.Fatalf("bound recent attack did not activate custom X: %+v", got)
	}
	if got := read(start.Add(23 * time.Second)); len(got) != 0 {
		t.Fatalf("combat-only rule survived the target 20-second window: %+v", got)
	}
	if err := session.ObserveMainAttack(3, start, true); err == nil {
		t.Fatal("unbound combat role accepted")
	}
}

func TestSeparateClientFlaskNeedsCurrentAreaAndMatchingMotion(t *testing.T) {
	engine, err := New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10,
			MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	session, err := NewFlaskSession(engine, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	if err := session.SetMenuGuard(2, "clear"); err != nil {
		t.Fatal(err)
	}
	start := time.Unix(100, 0)
	number := func(value float64) *float64 { return &value }
	read := func(after time.Duration, hash uint32, life float64) []string {
		t.Helper()
		at := start.Add(after)
		sample := telemetry.Sample{ProcessID: 202, SeenUTC: at, ReceivedAt: at,
			TickMS: at.UnixMilli(), CoordinateSpace: "world", AreaHash: hash,
			Life: number(life), LifeMax: number(100),
			Mana: number(100), ManaMax: number(100)}
		if !engine.ObserveSample(sample) {
			t.Fatal("fixture motion was rejected")
		}
		activations, err := session.ObserveSample(sample, at)
		if err != nil {
			t.Fatal(err)
		}
		resources := make([]string, len(activations))
		for i, activation := range activations {
			resources[i] = activation.Resource
		}
		return resources
	}
	if got := read(0, 55, 30); len(got) != 0 {
		t.Fatalf("missing area log requested a flask: %v", got)
	}
	engine.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_4", Seed: 123,
		ObservedAt: start.Add(time.Millisecond), Origin: "live"})
	if got := read(2*time.Millisecond, 0, 30); len(got) != 0 {
		t.Fatalf("zero area hash requested a flask: %v", got)
	}
	if got := read(3*time.Millisecond, 55, 30); len(got) != 1 || got[0] != "life" {
		t.Fatalf("fresh area-bound life did not trigger: %v", got)
	}
	engine.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_5", Seed: 456,
		ObservedAt: start.Add(4 * time.Millisecond), Origin: "live"})
	if got := read(5*time.Millisecond, 0, 80); len(got) != 0 {
		t.Fatalf("area transition requested a flask: %v", got)
	}
	if got := read(61*time.Second, 55, 30); len(got) != 0 {
		t.Fatalf("expired area log requested a flask: %v", got)
	}
}
