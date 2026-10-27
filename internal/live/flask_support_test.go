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
