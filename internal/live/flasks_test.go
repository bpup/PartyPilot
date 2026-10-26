package live

import (
	"testing"
	"time"

	"partypilot/internal/telemetry"
)

func TestLiveFlasksRequireBoundFreshVitalsAndClearMenu(t *testing.T) {
	session, err := NewFlaskSession(map[int]int{1: 101, 2: 202}, 750*time.Millisecond)
	if err != nil {
		t.Fatal(err)
	}
	start := time.Unix(100, 0)
	number := func(value float64) *float64 { return &value }
	sample := func(pid int, at time.Time, life float64) telemetry.Sample {
		return telemetry.Sample{ProcessID: pid, SeenUTC: at,
			Life: number(life), LifeMax: number(100), Mana: number(100), ManaMax: number(100)}
	}
	check := func(item telemetry.Sample, now time.Time, expected int) {
		t.Helper()
		got, err := session.ObserveSample(item, now)
		if err != nil || len(got) != expected {
			t.Fatalf("flask count %d, expected %d: %+v %v", len(got), expected, got, err)
		}
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
