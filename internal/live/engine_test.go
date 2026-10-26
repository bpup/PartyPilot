package live

import (
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
	"partypilot/internal/telemetry"
)

func TestLiveFollowRequiresFreshMatchingLogAndNewSamples(t *testing.T) {
	now := time.Now().UTC()
	config := follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}
	engine, err := New(map[int]int{1: 101, 2: 202}, 1, config, 10*time.Second)
	if err != nil {
		t.Fatal(err)
	}
	state := func(at time.Time) follow.State { return engine.Step(at)[2].State }
	if state(now) != follow.Waiting {
		t.Fatal("missing sources must wait")
	}
	engine.ObserveSample(telemetry.Sample{ProcessID: 101, SeenUTC: now, X: 12})
	engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: now, X: 0})
	if state(now) != follow.Waiting {
		t.Fatal("position alone must not enable following")
	}
	logAt := now.Add(time.Millisecond)
	for _, slot := range []int{1, 2} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123, ObservedAt: logAt, Origin: "live"})
	}
	if state(logAt) != follow.Waiting {
		t.Fatal("samples predating area evidence must wait")
	}
	sampleAt := now.Add(2 * time.Millisecond)
	engine.ObserveSample(telemetry.Sample{ProcessID: 101, SeenUTC: sampleAt, X: 12})
	engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: sampleAt, X: 0})
	if state(sampleAt) != follow.CatchingUp {
		t.Fatal("fresh matching sources must produce catch-up decision")
	}
	engine.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_4", Seed: 456, ObservedAt: sampleAt, Origin: "live"})
	engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: sampleAt.Add(time.Millisecond), X: 0})
	if state(sampleAt.Add(time.Millisecond)) != follow.Waiting {
		t.Fatal("different instance must stop")
	}
	engine.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_4", Seed: 123, ObservedAt: sampleAt, Origin: "history"})
	if state(sampleAt.Add(time.Millisecond)) != follow.Waiting {
		t.Fatal("history must not enable controller decisions")
	}
	engine.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_4", Seed: 123, ObservedAt: sampleAt.Add(time.Millisecond), Origin: "live"})
	engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: sampleAt.Add(2 * time.Millisecond), X: 0})
	later := now.Add(12 * time.Second)
	engine.ObserveSample(telemetry.Sample{ProcessID: 101, SeenUTC: later, X: 12})
	engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: later, X: 0})
	if state(later) != follow.Waiting {
		t.Fatal("expired area evidence must stop")
	}
}

func TestLiveFollowRejectsUnknownAndOldSamples(t *testing.T) {
	now := time.Now().UTC()
	config := follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}
	engine, err := New(map[int]int{1: 101, 2: 202}, 1, config, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	if engine.ObserveSample(telemetry.Sample{ProcessID: 303, SeenUTC: now}) ||
		!engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: now}) ||
		engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(-time.Second)}) {
		t.Fatal("sample identity or ordering failed")
	}
	if engine.ObserveArea(arealog.Signal{Slot: 3, AreaID: "x", Seed: 1, ObservedAt: now}) {
		t.Fatal("unknown area slot accepted")
	}
}
