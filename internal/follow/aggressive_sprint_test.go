package follow

import (
	"testing"
	"time"
)

func TestAggressiveSprintReleaseAndRetry(t *testing.T) {
	var sprint aggressiveSprint
	start := time.Unix(100, 0)
	step := func(ms int, safe bool, direction point) bool {
		return sprint.Update(true, safe, direction, point{0, 0}, 50,
			start.Add(time.Duration(ms)*time.Millisecond), 800*time.Millisecond)
	}
	forward := point{1, 0}
	if step(0, true, forward) || step(149, true, forward) {
		t.Fatal("dodge pressed before direction was stable for 150 ms")
	}
	if !step(150, true, forward) || !step(500, true, forward) {
		t.Fatal("dodge was not held after stable engagement")
	}
	if step(501, true, point{-1, 0}) {
		t.Fatal("major turn did not release the dodge button")
	}
	if step(600, true, forward) || step(700, true, forward) || step(850, true, forward) {
		t.Fatal("dodge retried before release gap, stable direction and cooldown")
	}
	if !step(950, true, forward) {
		t.Fatal("dodge did not retry after cooldown")
	}
	if sprint.Update(false, true, forward, point{}, 50, start.Add(time.Second), 800*time.Millisecond) ||
		sprint.phase != aggressiveIdle {
		t.Fatal("inactive follower did not reset sprint")
	}
}

func TestAggressiveSprintUnsafeDebounce(t *testing.T) {
	var sprint aggressiveSprint
	start := time.Unix(200, 0)
	step := func(ms int, safe bool) bool {
		return sprint.Update(true, safe, point{1, 0}, point{0, 0}, 50,
			start.Add(time.Duration(ms)*time.Millisecond), 800*time.Millisecond)
	}
	step(0, true)
	if !step(150, true) || !step(450, false) || step(550, false) {
		t.Fatal("unsafe corridor did not use the 100 ms debounce")
	}
}
