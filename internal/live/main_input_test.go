package live

import (
	"testing"
	"time"

	"partypilot/internal/follow"
)

func TestMainInputRequiresOptInCurrentLeaderAndFreshSample(t *testing.T) {
	engine, err := New(map[int]int{1: 101, 2: 202}, 1,
		follow.Config{StopDistance: 9, ResumeDistance: 12, CatchUpDistance: 90,
			MaxAge: time.Second}, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	at := time.Unix(1000, 0).UTC()
	input := MainInput{Slot: 1, LeftX: 0.5, LeftY: -0.5, BDown: true, ObservedAt: at}
	if engine.ObserveMainInput(input, at) {
		t.Fatal("main input was accepted before opt-in")
	}
	engine.SetAcceptMainInput(true)
	if !engine.ObserveMainInput(input, at) {
		t.Fatal("current main input was rejected")
	}
	if got, ok := engine.CurrentMainInput(at.Add(500 * time.Millisecond)); !ok || got != input {
		t.Fatalf("fresh main input was not retained: %+v %v", got, ok)
	}
	if _, ok := engine.CurrentMainInput(at.Add(501 * time.Millisecond)); ok {
		t.Fatal("expired main input was retained")
	}
	input.Slot = 2
	if engine.ObserveMainInput(input, at) {
		t.Fatal("follower input was accepted as the main controller")
	}
	engine.SetFollowPaused(true)
	if _, ok := engine.CurrentMainInput(at); ok {
		t.Fatal("pause retained main controller input")
	}
}
