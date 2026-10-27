package slotaudit

import (
	"math"
	"testing"
	"time"

	"partypilot/internal/live"
)

func TestControlledMovementMeasuresEachSlotWithoutVerdict(t *testing.T) {
	start := time.Date(2026, 10, 27, 0, 0, 0, 0, time.UTC)
	a := New()
	frame := func(second int, area uint32, first, secondPlayer float64) live.OrderedClientFrame {
		at := start.Add(time.Duration(second) * time.Second)
		return live.OrderedClientFrame{ProcessID: 17, SeenUTC: at, TickMS: at.UnixMilli(),
			AreaHash: area, CoordinateSpace: "world",
			PlayerOrderSource: "unverified_exilecore_local_slot_candidate",
			Players: []live.OrderedLocalPlayer{{LocalPlayer: 1, X: first},
				{LocalPlayer: 2, X: secondPlayer}}}
	}
	for _, f := range []live.OrderedClientFrame{
		frame(0, 1, 0, 0), frame(1, 1, 1, 0), frame(2, 1, 3, 0),
		frame(3, 2, 100, 100), frame(4, 2, 100, 102), frame(5, 2, 100, 105),
	} {
		if !a.ObserveFast(f) {
			t.Fatal("candidate frame rejected")
		}
	}
	reports := a.ControlledMovement([]MovementWindow{
		{Label: "P1 only", Start: 0, End: 3 * time.Second},
		{Label: "P2 only", Start: 3 * time.Second, End: 6 * time.Second},
	})[17]
	if len(reports) != 2 || reports[0].CompleteFrames != 3 ||
		reports[0].SameAreaSteps != 2 || reports[0].PathDistanceWorld != [2]float64{3, 0} ||
		reports[1].CompleteFrames != 3 || reports[1].SameAreaSteps != 2 ||
		reports[1].PathDistanceWorld != [2]float64{0, 5} {
		t.Fatalf("wrong controlled movement diagnostics: %+v", reports)
	}
	stale := frame(4, 2, math.NaN(), 9)
	if a.ObserveFast(stale) {
		t.Fatal("non-finite candidate accepted")
	}
}
