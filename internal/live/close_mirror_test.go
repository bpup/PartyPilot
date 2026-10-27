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

func TestLiveCloseMirrorHandsOffOnExpiryInSameFrame(t *testing.T) {
	at := time.Unix(2200, 0).UTC()
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
	for _, sample := range []telemetry.Sample{
		{ProcessID: 101, SeenUTC: at.Add(time.Millisecond), X: 9},
		{ProcessID: 202, SeenUTC: at.Add(time.Millisecond)},
	} {
		if !observeGridSample(engine, sample) {
			t.Fatal("initial position rejected")
		}
	}
	start := at.Add(time.Millisecond)
	engine.SetAcceptMainInput(true)
	engine.SetCloseRangeMirror(true, 400*time.Millisecond)
	engine.ObserveMainInput(MainInput{Slot: 1, LeftX: 1, ObservedAt: start}, start)
	if got := engine.Step(start)[2]; !got.MirrorMovement {
		t.Fatalf("mirror burst did not start: %+v", got)
	}
	moved := start.Add(350 * time.Millisecond)
	if !observeGridSample(engine, telemetry.Sample{ProcessID: 101, SeenUTC: moved, X: 10}) ||
		!observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: moved}) {
		t.Fatal("moving frame rejected")
	}
	engine.ObserveMainInput(MainInput{Slot: 1, LeftX: 1, ObservedAt: moved}, moved)
	if got := engine.Step(moved)[2]; !got.MirrorMovement {
		t.Fatalf("burst ended before configured duration: %+v", got)
	}
	expired := start.Add(400 * time.Millisecond)
	engine.ObserveMainInput(MainInput{Slot: 1, LeftX: 1, ObservedAt: expired}, expired)
	if got := engine.Step(expired)[2]; got.State != follow.Following || got.MirrorMovement ||
		got.X <= 0 || got.Y <= 0 {
		t.Fatalf("mirror expiry did not transfer movement in the same frame: %+v", got)
	}
}

func TestLiveCloseMirrorReleasesConfiguredBAtEight(t *testing.T) {
	at := time.Unix(2300, 0).UTC()
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
	start := at.Add(time.Millisecond)
	for _, sample := range []telemetry.Sample{
		{ProcessID: 101, SeenUTC: start, X: 12},
		{ProcessID: 202, SeenUTC: start},
	} {
		if !observeGridSample(engine, sample) {
			t.Fatal("initial position rejected")
		}
	}
	engine.SetAcceptMainInput(true)
	engine.SetCloseRangeMirror(true, 400*time.Millisecond)
	engine.ObserveMainInput(MainInput{Slot: 1, LeftX: 1, BDown: true, ObservedAt: start}, start)
	if got := engine.Step(start)[2]; got.MirrorMovement || got.SuppressMirroredB {
		t.Fatalf("disabled follower B started close mirror: %+v", got)
	}
	if err := engine.SetCloseMirrorBAllowed(2, true); err != nil {
		t.Fatal(err)
	}
	if got := engine.Step(start)[2]; got.Reason != "Mirrored dodge / sprint" ||
		!got.MirrorMovement || got.SuppressMirroredB {
		t.Fatalf("configured B did not start mirrored dodge: %+v", got)
	}
	near := start.Add(150 * time.Millisecond)
	if !observeGridSample(engine, telemetry.Sample{ProcessID: 101, SeenUTC: near, X: 8}) ||
		!observeGridSample(engine, telemetry.Sample{ProcessID: 202, SeenUTC: near}) {
		t.Fatal("near frame rejected")
	}
	engine.ObserveMainInput(MainInput{Slot: 1, LeftX: 1, BDown: true, ObservedAt: near}, near)
	if got := engine.Step(near)[2]; !got.SuppressMirroredB || got.MirrorMovement {
		t.Fatalf("mirrored B was not released at eight grid units: %+v", got)
	}
	released := near.Add(50 * time.Millisecond)
	engine.ObserveMainInput(MainInput{Slot: 1, LeftX: 1, ObservedAt: released}, released)
	if got := engine.Step(released)[2]; got.SuppressMirroredB {
		t.Fatalf("physical B release kept suppression latched: %+v", got)
	}
}
