package live

import (
	"math"
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
	"partypilot/internal/partylog"
	"partypilot/internal/telemetry"
)

func TestIsometricStickMatchesSuppliedFollowDirection(t *testing.T) {
	for _, test := range []struct {
		gridX, gridY   float64
		stickX, stickY float64
	}{
		{1, 0, 1 / math.Sqrt2, 1 / math.Sqrt2},
		{0, 1, -1 / math.Sqrt2, 1 / math.Sqrt2},
		{-1, 0, -1 / math.Sqrt2, -1 / math.Sqrt2},
	} {
		x, y := isometricStick(test.gridX, test.gridY)
		if math.Abs(x-test.stickX) > 1e-12 || math.Abs(y-test.stickY) > 1e-12 {
			t.Fatalf("grid (%v,%v) mapped to stick (%v,%v)", test.gridX, test.gridY, x, y)
		}
	}
}

func observeGridSample(engine *Engine, sample telemetry.Sample) bool {
	sample.CoordinateSpace = "world"
	sample.X *= worldPerGridUnit
	sample.Y *= worldPerGridUnit
	return engine.ObserveSample(sample)
}

func TestLiveFollowRequiresKnownWorldCoordinateSpace(t *testing.T) {
	now := time.Now().UTC()
	engine, err := New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	if engine.ObserveSample(telemetry.Sample{ProcessID: 101, SeenUTC: now, X: 120}) {
		t.Fatal("unmarked position was accepted for movement")
	}
	for _, slot := range []int{1, 2} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: now, Origin: "live"})
	}
	observeGridSample(engine, telemetry.Sample{ProcessID: 101, SeenUTC: now.Add(time.Millisecond), X: 12})
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(time.Millisecond), X: 0})
	command := engine.Step(now.Add(time.Millisecond))[2]
	if command.State != follow.CatchingUp || command.Distance < 11.99 || command.Distance > 12.01 {
		t.Fatalf("world coordinates were not converted to target grid distance: %+v", command)
	}
	if math.Abs(command.X-1/math.Sqrt2) > 1e-12 ||
		math.Abs(command.Y-1/math.Sqrt2) > 1e-12 {
		t.Fatalf("live movement did not use the isometric stick direction: %+v", command)
	}
}

func TestLiveFollowTurnsAlongRecordedLeaderTrail(t *testing.T) {
	now := time.Now().UTC()
	engine, err := New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 8, ResumeDistance: 9, CatchUpDistance: 30,
			MaxAge: time.Second, TargetTrail: true}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	for _, slot := range []int{1, 2} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: now, Origin: "live"})
	}
	steps := []struct {
		leaderX, leaderY, followerX, followerY float64
	}{
		{0, 0, -20, 0},
		{10, 0, -20, 0},
		{10, 10, 12, -2},
	}
	for index, step := range steps {
		at := now.Add(time.Duration(index+1) * time.Millisecond)
		observeGridSample(engine, telemetry.Sample{ProcessID: 101, SeenUTC: at,
			X: step.leaderX, Y: step.leaderY})
		observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: at,
			X: step.followerX, Y: step.followerY})
		command := engine.Step(at)[2]
		if index == len(steps)-1 && (command.State != follow.CatchingUp ||
			command.X > -0.9 || command.Y < 0.2 || command.Y > 0.4) {
			t.Fatalf("live movement skipped the recorded bend or isometric transform: %+v", command)
		}
	}
}

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
	observeGridSample(engine, telemetry.Sample{ProcessID: 101, SeenUTC: now, X: 12})
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: now, X: 0})
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
	observeGridSample(engine, telemetry.Sample{ProcessID: 101, SeenUTC: sampleAt, X: 12})
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: sampleAt, X: 0})
	if state(sampleAt) != follow.CatchingUp {
		t.Fatal("fresh matching sources must produce catch-up decision")
	}
	engine.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_4", Seed: 456, ObservedAt: sampleAt, Origin: "live"})
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: sampleAt.Add(time.Millisecond), X: 0})
	if state(sampleAt.Add(time.Millisecond)) != follow.Waiting {
		t.Fatal("different instance must stop")
	}
	engine.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_4", Seed: 123, ObservedAt: sampleAt, Origin: "history"})
	if state(sampleAt.Add(time.Millisecond)) != follow.Waiting {
		t.Fatal("history must not enable controller decisions")
	}
	engine.ObserveArea(arealog.Signal{Slot: 2, AreaID: "G2_4", Seed: 123, ObservedAt: sampleAt.Add(time.Millisecond), Origin: "live"})
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: sampleAt.Add(2 * time.Millisecond), X: 0})
	later := now.Add(12 * time.Second)
	observeGridSample(engine, telemetry.Sample{ProcessID: 101, SeenUTC: later, X: 12})
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: later, X: 0})
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
		observeGridSample(engine, telemetry.Sample{ProcessID: pid, SeenUTC: now.Add(time.Millisecond), X: x})
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
	observeGridSample(engine, telemetry.Sample{ProcessID: 101, SeenUTC: now.Add(2 * time.Millisecond), X: 12})
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(2 * time.Millisecond), X: 0})
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
		observeGridSample(engine, telemetry.Sample{ProcessID: pid, SeenUTC: now.Add(time.Millisecond), X: x})
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

func TestLiveFollowUsesConfiguredTargetAndCurrentMainFallback(t *testing.T) {
	now := time.Now().UTC()
	config := follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}
	engine, err := New(map[int]int{1: 101, 2: 202, 3: 303}, 1, config, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	for slot, pid := range map[int]int{1: 101, 2: 202, 3: 303} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: now, Origin: "live"})
		observeGridSample(engine, telemetry.Sample{ProcessID: pid, SeenUTC: now.Add(time.Millisecond),
			X: map[int]float64{1: 0, 2: 20, 3: 30}[slot]})
	}
	at := now.Add(time.Millisecond)
	if command := engine.Step(at)[2]; command.X >= 0 {
		t.Fatalf("default MAIN route: %+v", command)
	}
	if err := engine.SetFollowTarget(2, 3); err != nil {
		t.Fatal(err)
	}
	if command := engine.Step(at)[2]; command.X <= 0 {
		t.Fatalf("selected P3 route: %+v", command)
	}
	if err := engine.SetFollowTarget(2, 2); err != nil {
		t.Fatal(err)
	}
	engine.SetLastFocusedLeads(true)
	if err := engine.ObserveManualFocus(3); err != nil {
		t.Fatal(err)
	}
	if command := engine.Step(at)[2]; command.X <= 0 {
		t.Fatalf("self choice did not follow new MAIN P3: %+v", command)
	}
}

func TestLiveFollowChecksTheSelectedTargetsInstance(t *testing.T) {
	now := time.Now().UTC()
	engine, err := New(map[int]int{1: 101, 2: 202, 3: 303}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	for slot, pid := range map[int]int{1: 101, 2: 202, 3: 303} {
		seed := int64(77)
		if slot == 1 {
			seed = 88
		}
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: seed,
			ObservedAt: now, Origin: "live"})
		observeGridSample(engine, telemetry.Sample{ProcessID: pid, SeenUTC: now.Add(time.Millisecond),
			X: map[int]float64{1: 0, 2: 0, 3: 20}[slot]})
	}
	at := now.Add(time.Millisecond)
	if command := engine.Step(at)[2]; command.State != follow.Waiting {
		t.Fatalf("default MAIN in another instance moved P2: %+v", command)
	}
	if err := engine.SetFollowTarget(2, 3); err != nil {
		t.Fatal(err)
	}
	if command := engine.Step(at)[2]; command.State != follow.CatchingUp || command.X <= 0 {
		t.Fatalf("P2 did not follow selected P3 in its own instance: %+v", command)
	}
	engine.ObserveArea(arealog.Signal{Slot: 3, AreaID: "G2_4", Seed: 99,
		ObservedAt: at.Add(time.Millisecond), Origin: "live"})
	observeGridSample(engine, telemetry.Sample{ProcessID: 303, SeenUTC: at.Add(2 * time.Millisecond), X: 20})
	if command := engine.Step(at.Add(2 * time.Millisecond))[2]; command.State != follow.Waiting {
		t.Fatalf("P2 moved after selected P3 entered another instance: %+v", command)
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

func TestThreeIndependentClientsFollowCurrentMain(t *testing.T) {
	now := time.Now().UTC()
	engine, err := New(map[int]int{1: 101, 2: 202, 3: 303}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	engine.SetLastFocusedLeads(true)
	for slot, pid := range map[int]int{1: 101, 2: 202, 3: 303} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: now, Origin: "live"})
		observeGridSample(engine, telemetry.Sample{ProcessID: pid, SeenUTC: now.Add(time.Millisecond),
			X: float64((slot - 1) * 10)})
	}
	initial := engine.Step(now.Add(time.Millisecond))
	if len(initial) != 2 || initial[2].State != follow.CatchingUp ||
		initial[3].State != follow.CatchingUp || initial[2].X >= 0 || initial[3].X >= 0 {
		t.Fatalf("three-client P1 MAIN decisions wrong: %+v", initial)
	}
	if err := engine.ObserveManualFocus(3); err != nil {
		t.Fatal(err)
	}
	switched := engine.Step(now.Add(time.Millisecond))
	if engine.Leader() != 3 || len(switched) != 2 ||
		switched[1].State != follow.CatchingUp || switched[2].State != follow.CatchingUp ||
		switched[1].X <= 0 || switched[2].X <= 0 {
		t.Fatalf("three-client P3 MAIN decisions wrong: %+v", switched)
	}
}

func TestLiveFollowRejectsUnknownAndOldSamples(t *testing.T) {
	now := time.Now().UTC()
	config := follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}
	engine, err := New(map[int]int{1: 101, 2: 202}, 1, config, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	if observeGridSample(engine, telemetry.Sample{ProcessID: 303, SeenUTC: now}) ||
		!observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: now}) ||
		observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(-time.Second)}) {
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
	observeGridSample(engine, telemetry.Sample{ProcessID: 101, SeenUTC: now.Add(time.Millisecond), X: 12})
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(time.Millisecond), X: 0, Life: &hp})
	if command := engine.Step(now.Add(time.Millisecond))[2]; command.State != follow.CatchingUp || !command.ManualOverrideEligible {
		t.Fatalf("living follower should allow right-stick movement: %+v", command)
	}
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(2 * time.Millisecond), X: 12, Life: &hp})
	if command := engine.Step(now.Add(2 * time.Millisecond))[2]; command.State != follow.Holding || !command.ManualOverrideEligible {
		t.Fatalf("living holding follower should allow manual movement: %+v", command)
	}
	dead := 0.0
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(3 * time.Millisecond), X: 0, Life: &dead})
	if command := engine.Step(now.Add(3 * time.Millisecond))[2]; command.ManualOverrideEligible || command.State != follow.Waiting || command.Reason != "dead follower" {
		t.Fatalf("dead follower should not receive automated or manual movement: %+v", command)
	}
	observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: now.Add(4 * time.Millisecond), X: 0})
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
	observeGridSample(engine, telemetry.Sample{ProcessID: 101, SeenUTC: now.Add(time.Millisecond), X: 12,
		VisiblePlayers: []telemetry.VisiblePlayer{
			{EntityID: 11, Name: "Leader", X: 12, IsObservedLocal: true},
			{EntityID: 12, Name: "Other", X: 0},
		}})
	if command := engine.Step(now.Add(time.Millisecond))[2]; command.State != follow.Waiting {
		t.Fatalf("visible entity candidate was treated as an identified follower: %+v", command)
	}
}
