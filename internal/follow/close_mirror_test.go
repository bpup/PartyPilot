package follow

import (
	"math"
	"testing"
	"time"
)

func TestCloseMirrorBurstAndContinuation(t *testing.T) {
	var mirror CloseMirror
	at := time.Unix(500, 0)
	input := CloseMirrorInput{Follow: Command{State: Holding, Distance: 9},
		LeftX: 1, LeftY: 1, Enabled: true, Duration: 800 * time.Millisecond,
		Now: at, FrameReceived: at, Area: "instance", Leader: 1}
	for offset := time.Duration(0); offset < 800*time.Millisecond; offset += 100 * time.Millisecond {
		input.Now, input.FrameReceived = at.Add(offset), at.Add(offset)
		got := mirror.Apply(input)
		if got.Reason != "Close-range mirror" || got.State != Holding ||
			math.Abs(got.X-math.Sqrt(0.5)) > 0.001 || math.Abs(got.Y-math.Sqrt(0.5)) > 0.001 {
			t.Fatalf("close-range burst at %v: %+v", offset, got)
		}
	}
	input.Now, input.FrameReceived = at.Add(800*time.Millisecond), at.Add(800*time.Millisecond)
	if got := mirror.Apply(input); got.X != 0 || got.Y != 0 || !mirror.ContinueWith(1, 1, true) {
		t.Fatalf("burst expiry did not request Smart Follow continuation: %+v", got)
	}
	input.LeftX, input.LeftY = 0, 0
	if got := mirror.Apply(input); got.X != 0 || mirror.ContinuousFollow {
		t.Fatalf("releasing main stick retained close mirroring: %+v", got)
	}
}

func TestCloseMirrorReleasesMirroredSprintAtEight(t *testing.T) {
	var mirror CloseMirror
	at := time.Unix(510, 0)
	input := CloseMirrorInput{Follow: Command{State: Following, Distance: 12},
		LeftX: 1, Enabled: true, Duration: 400 * time.Millisecond,
		Now: at, FrameReceived: at, Area: "instance", Leader: 1,
		MirroredSprintHeld: true, WithinCloseRange: true}
	if got := mirror.Apply(input); got.Reason != "Mirrored dodge / sprint" || !mirror.mirroringSprint {
		t.Fatalf("close sprint did not start movement mirroring: %+v", got)
	}
	input.Follow.Distance = 8
	input.Now, input.FrameReceived = at.Add(100*time.Millisecond), at.Add(100*time.Millisecond)
	if got := mirror.Apply(input); mirror.SuppressMirroredSprint() || got.Reason != "Mirrored dodge / sprint" {
		t.Fatalf("mirrored sprint released before its minimum press: %+v", got)
	}
	input.Now, input.FrameReceived = at.Add(150*time.Millisecond), at.Add(150*time.Millisecond)
	if got := mirror.Apply(input); !mirror.SuppressMirroredSprint() || !mirror.ContinuousFollow ||
		got.Reason == "Mirrored dodge / sprint" {
		t.Fatalf("mirrored sprint did not release at eight units: %+v", got)
	}
	input.MirroredSprintHeld = false
	mirror.Apply(input)
	if mirror.SuppressMirroredSprint() {
		t.Fatal("releasing the physical sprint button kept suppression latched")
	}
}

func TestCloseMirrorRejectsStaleFrameAndLeaderChange(t *testing.T) {
	var mirror CloseMirror
	at := time.Unix(520, 0)
	input := CloseMirrorInput{Follow: Command{State: Holding, Distance: 9},
		LeftX: 1, Enabled: true, Duration: 400 * time.Millisecond,
		Now: at, FrameReceived: at, Area: "instance", Leader: 1}
	if got := mirror.Apply(input); got.Reason != "Close-range mirror" {
		t.Fatalf("fixture did not start mirror burst: %+v", got)
	}
	input.Now = at.Add(600 * time.Millisecond)
	if got := mirror.Apply(input); got.X != 0 || mirror.ContinuousFollow {
		t.Fatalf("stale frame kept physical movement: %+v", got)
	}
	input.FrameReceived = input.Now
	input.Leader = 2
	if got := mirror.Apply(input); got.Reason != "Close-range mirror" ||
		mirror.leader != 2 || mirror.until != input.Now.Add(400*time.Millisecond) {
		t.Fatalf("new leader did not start its own mirror burst: %+v", got)
	}
}
