package live

import (
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
	"partypilot/internal/partylog"
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

func TestDepartureLatchesStopUntilSessionRestart(t *testing.T) {
	now := time.Now().UTC()
	config := follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}
	engine, err := New(map[int]int{1: 101, 2: 202}, 1, config, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	engine.SetLeaderName("Leader")
	for slot, pid := range map[int]int{1: 101, 2: 202} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123, ObservedAt: now, Origin: "live"})
		x := float64(0)
		if slot == 1 {
			x = 12
		}
		engine.ObserveSample(telemetry.Sample{ProcessID: pid, SeenUTC: now.Add(time.Millisecond), X: x})
	}
	if engine.Step(now.Add(time.Millisecond))[2].State != follow.CatchingUp {
		t.Fatal("expected live decision before party departure")
	}
	if engine.ObserveParty(partylog.Event{Kind: partylog.MemberLeft, ClientSlot: 2, MemberName: "Other"}) ||
		engine.Step(now.Add(time.Millisecond))[2].State != follow.CatchingUp {
		t.Fatal("unrelated member departure stopped following")
	}
	if !engine.ObserveParty(partylog.Event{Kind: partylog.MemberLeft, ClientSlot: 2, MemberName: "leader"}) {
		t.Fatal("configured leader departure did not stop")
	}
	engine.ObserveSample(telemetry.Sample{ProcessID: 101, SeenUTC: now.Add(2 * time.Millisecond), X: 12})
	engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(2 * time.Millisecond), X: 0})
	command := engine.Step(now.Add(2 * time.Millisecond))[2]
	if command.State != follow.Waiting || command.Reason != "followed member left party" {
		t.Fatalf("departure stop did not latch: %+v", command)
	}
	for _, kind := range []partylog.Kind{partylog.Disbanded, partylog.LocalLeft} {
		other, err := New(map[int]int{1: 101, 2: 202}, 1, config, time.Minute)
		if err != nil || !other.ObserveParty(partylog.Event{Kind: kind, ClientSlot: 2}) ||
			other.Step(now)[2].State != follow.Waiting {
			t.Fatalf("departure %s was not stopped", kind)
		}
	}
}

func TestManualFocusChangesLiveMainAndFormerLeaderFollows(t *testing.T) {
	now := time.Now().UTC()
	config := follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}
	engine, err := New(map[int]int{1: 101, 2: 202}, 1, config, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	engine.SetLastFocusedLeads(true)
	for slot, pid := range map[int]int{1: 101, 2: 202} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123, ObservedAt: now, Origin: "live"})
		x := float64(0)
		if slot == 2 {
			x = 12
		}
		engine.ObserveSample(telemetry.Sample{ProcessID: pid, SeenUTC: now.Add(time.Millisecond), X: x})
	}
	if engine.ObserveManualFocus(3) == nil || engine.Leader() != 1 {
		t.Fatal("unknown manual MAIN accepted")
	}
	if err := engine.ObserveManualFocus(2); err != nil {
		t.Fatal(err)
	}
	commands := engine.Step(now.Add(time.Millisecond))
	if engine.Leader() != 2 || commands[1].State != follow.CatchingUp || commands[1].X <= 0 {
		t.Fatalf("former MAIN did not follow the manually focused P2: %+v", commands)
	}
	if err := engine.ObserveManualFocus(1); err != nil {
		t.Fatal(err)
	}
	if engine.Leader() != 1 || engine.Step(now.Add(time.Millisecond))[2].X >= 0 {
		t.Fatal("manual P1 focus did not restore P1 MAIN")
	}
}

func TestNamedDepartureTracksCurrentManualMain(t *testing.T) {
	config := follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}
	engine, err := New(map[int]int{1: 101, 2: 202}, 1, config, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	engine.SetLastFocusedLeads(true)
	engine.SetLeaderName("First")
	if err := engine.SetMemberName(2, "Second"); err != nil {
		t.Fatal(err)
	}
	if err := engine.ObserveManualFocus(2); err != nil {
		t.Fatal(err)
	}
	if engine.ObserveParty(partylog.Event{Kind: partylog.MemberLeft, ClientSlot: 1, MemberName: "First"}) {
		t.Fatal("former MAIN departure stopped the current follow target")
	}
	if !engine.ObserveParty(partylog.Event{Kind: partylog.MemberLeft, ClientSlot: 1, MemberName: "second"}) ||
		engine.Step(time.Now().UTC())[1].Reason != "followed member left party" {
		t.Fatal("current P2 MAIN departure did not stop following")
	}
	if err := engine.SetMemberName(3, "Unknown"); err == nil {
		t.Fatal("unknown member slot accepted")
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

func TestManualOverrideRequiresFreshLivingFollower(t *testing.T) {
	now := time.Now().UTC()
	config := follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}
	engine, err := New(map[int]int{1: 101, 2: 202}, 1, config, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	for _, slot := range []int{1, 2} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123, ObservedAt: now, Origin: "live"})
	}
	hp := 100.0
	engine.ObserveSample(telemetry.Sample{ProcessID: 101, SeenUTC: now.Add(time.Millisecond), X: 12})
	engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(time.Millisecond), X: 0, Life: &hp})
	if command := engine.Step(now.Add(time.Millisecond))[2]; command.State != follow.CatchingUp || !command.ManualOverrideEligible {
		t.Fatalf("living follower should allow right-stick movement: %+v", command)
	}
	engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(2 * time.Millisecond), X: 12, Life: &hp})
	if command := engine.Step(now.Add(2 * time.Millisecond))[2]; command.State != follow.Holding || !command.ManualOverrideEligible {
		t.Fatalf("living holding follower should allow manual movement: %+v", command)
	}
	dead := 0.0
	engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(3 * time.Millisecond), X: 0, Life: &dead})
	if command := engine.Step(now.Add(3 * time.Millisecond))[2]; command.ManualOverrideEligible || command.State != follow.Waiting || command.Reason != "dead follower" {
		t.Fatalf("dead follower should not receive automated or manual movement: %+v", command)
	}
	engine.ObserveSample(telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(4 * time.Millisecond), X: 0})
	if command := engine.Step(now.Add(4 * time.Millisecond))[2]; command.ManualOverrideEligible {
		t.Fatalf("missing life data should not allow manual movement: %+v", command)
	}
	if command := engine.Step(now.Add(2 * time.Second))[2]; command.ManualOverrideEligible {
		t.Fatalf("stale follower should not allow manual movement: %+v", command)
	}
}

func TestVisiblePlayerDiagnosticDoesNotBecomeFollowerEvidence(t *testing.T) {
	now := time.Now().UTC()
	config := follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}
	engine, err := New(map[int]int{1: 101, 2: 202}, 1, config, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	for _, slot := range []int{1, 2} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123, ObservedAt: now, Origin: "live"})
	}
	engine.ObserveSample(telemetry.Sample{ProcessID: 101, SeenUTC: now.Add(time.Millisecond), X: 12,
		VisiblePlayers: []telemetry.VisiblePlayer{
			{EntityID: 11, Name: "Leader", X: 12, IsObservedLocal: true},
			{EntityID: 12, Name: "Other", X: 0},
		}})
	if command := engine.Step(now.Add(time.Millisecond))[2]; command.State != follow.Waiting {
		t.Fatalf("visible entity candidate was treated as an identified follower: %+v", command)
	}
}
