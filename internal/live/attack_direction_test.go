package live

import (
	"math"
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
	"partypilot/internal/telemetry"
)

func TestMirrorAttackDirectionRespectsCatchUpPriorityAndInputSafety(t *testing.T) {
	at := time.Unix(2300, 0).UTC()
	engine, err := New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 9, ResumeDistance: 12, CatchUpDistance: 20,
			MaxAge: time.Second, TargetTrail: true, NormalSprintCatchUp: true}, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	for _, slot := range []int{1, 2} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: at, Origin: "live"})
	}
	for _, sample := range []telemetry.Sample{
		{ProcessID: 101, SeenUTC: at.Add(time.Millisecond), X: 40},
		{ProcessID: 202, SeenUTC: at.Add(time.Millisecond)},
	} {
		if !observeGridSample(engine, sample) {
			t.Fatal("valid position was rejected")
		}
	}
	frameAt := at.Add(time.Millisecond)
	engine.SetAcceptMainInput(true)
	engine.SetMirrorAttackDirection(true, true)
	if !engine.ObserveMainInput(MainInput{Slot: 1, LeftY: 1, AttackActive: true,
		ObservedAt: frameAt}, frameAt) {
		t.Fatal("attack input was rejected")
	}
	if got := engine.Step(frameAt)[2]; got.State != follow.CatchingUp || got.AttackDirection ||
		math.Abs(got.X-got.Y) > 0.001 {
		t.Fatalf("catch-up priority did not protect follower movement: %+v", got)
	}
	engine.SetMirrorAttackDirection(true, false)
	if got := engine.Step(frameAt)[2]; got.State != follow.CatchingUp || !got.AttackDirection ||
		got.X != 0 || got.Y != 1 || got.SprintDown {
		t.Fatalf("attack direction did not override catch-up without priority: %+v", got)
	}
	stillAt := frameAt.Add(250 * time.Millisecond)
	for _, sample := range []telemetry.Sample{
		{ProcessID: 101, SeenUTC: stillAt, X: 40},
		{ProcessID: 202, SeenUTC: stillAt},
	} {
		if !observeGridSample(engine, sample) {
			t.Fatal("stable sprint sample was rejected")
		}
	}
	engine.ObserveMainInput(MainInput{Slot: 1, AttackActive: true,
		ObservedAt: stillAt}, stillAt)
	if got := engine.Step(stillAt)[2]; got.State != follow.CatchingUp || got.AttackDirection ||
		got.SprintDown {
		t.Fatalf("held attack did not release synthetic sprint without a stick direction: %+v", got)
	}
	blockedAt := stillAt.Add(50 * time.Millisecond)
	engine.ObserveMainInput(MainInput{Slot: 1, LeftY: 1, AttackActive: true,
		Blocked: true, ObservedAt: blockedAt}, blockedAt)
	if got := engine.Step(blockedAt)[2]; got.AttackDirection {
		t.Fatalf("blocked attack input moved the follower: %+v", got)
	}
	engine.SetFollowPaused(true)
	if got := engine.Step(blockedAt)[2]; got.State != follow.Waiting || got.AttackDirection {
		t.Fatalf("pause retained attack direction: %+v", got)
	}
}

func TestMirrorAttackDirectionCanMoveHoldingFollower(t *testing.T) {
	at := time.Unix(2400, 0).UTC()
	engine, err := New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 10, ResumeDistance: 13, CatchUpDistance: 90,
			MaxAge: time.Second, TargetTrail: true}, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	for _, slot := range []int{1, 2} {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: at, Origin: "live"})
	}
	frameAt := at.Add(time.Millisecond)
	for _, sample := range []telemetry.Sample{
		{ProcessID: 101, SeenUTC: frameAt, X: 9},
		{ProcessID: 202, SeenUTC: frameAt},
	} {
		if !observeGridSample(engine, sample) {
			t.Fatal("position was rejected")
		}
	}
	engine.SetAcceptMainInput(true)
	engine.SetMirrorAttackDirection(true, true)
	engine.ObserveMainInput(MainInput{Slot: 1, LeftY: 1, AttackActive: true,
		ObservedAt: frameAt}, frameAt)
	if got := engine.Step(frameAt)[2]; got.State != follow.Holding || !got.AttackDirection ||
		got.X != 0 || got.Y != 1 {
		t.Fatalf("holding follower did not mirror attack direction: %+v", got)
	}
}
