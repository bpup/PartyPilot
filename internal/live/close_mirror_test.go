package live

import (
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
	"partypilot/internal/telemetry"
)

func TestLiveCloseMirrorRequiresFreshMainInputAndReleasesOnPause(t *testing.T) {
	at := time.Unix(2000, 0).UTC()
	engine, err := New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 10, ResumeDistance: 13, CatchUpDistance: 90,
			MaxAge: time.Second}, time.Second)
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
		{ProcessID: 202, SeenUTC: frameAt, X: 0},
	} {
		if !observeGridSample(engine, sample) {
			t.Fatal("valid position was rejected")
		}
	}
	engine.SetCloseRangeMirror(true, 400*time.Millisecond)
	if got := engine.Step(frameAt)[2]; got.State != follow.Holding || got.MirrorMovement {
		t.Fatalf("mirror ran without subscribed input: %+v", got)
	}
	engine.SetAcceptMainInput(true)
	if !engine.ObserveMainInput(MainInput{Slot: 1, LeftX: 1, ObservedAt: frameAt}, frameAt) {
		t.Fatal("fresh main-controller input rejected")
	}
	if got := engine.Step(frameAt)[2]; got.State != follow.Holding || !got.MirrorMovement ||
		got.X != 1 || got.Y != 0 {
		t.Fatalf("holding follower did not receive mirror direction: %+v", got)
	}
	releaseAt := frameAt.Add(50 * time.Millisecond)
	if !engine.ObserveMainInput(MainInput{Slot: 1, ObservedAt: releaseAt}, releaseAt) {
		t.Fatal("main-controller release was rejected")
	}
	if got := engine.Step(releaseAt)[2]; got.MirrorMovement || got.X != 0 || got.Y != 0 {
		t.Fatalf("released stick kept mirror movement: %+v", got)
	}
	pressAt := frameAt.Add(100 * time.Millisecond)
	if !engine.ObserveMainInput(MainInput{Slot: 1, LeftX: 1, ObservedAt: pressAt}, pressAt) {
		t.Fatal("renewed main-controller press was rejected")
	}
	if got := engine.Step(pressAt)[2]; !got.MirrorMovement {
		t.Fatalf("new stick edge did not restart mirror: %+v", got)
	}
	if got := engine.Step(pressAt.Add(501 * time.Millisecond))[2]; got.MirrorMovement {
		t.Fatalf("expired main-controller input kept mirror movement: %+v", got)
	}
	engine.SetFollowPaused(true)
	if got := engine.Step(frameAt.Add(time.Millisecond))[2]; got.State != follow.Waiting || got.MirrorMovement {
		t.Fatalf("pause retained close mirror: %+v", got)
	}
	engine.SetFollowPaused(false)
	if got := engine.Step(frameAt.Add(2 * time.Millisecond))[2]; got.MirrorMovement {
		t.Fatalf("resume reused old controller input: %+v", got)
	}
}
