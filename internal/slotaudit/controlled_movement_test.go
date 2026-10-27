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
			AreaInstanceAddress: "0x1000", VectorHeaderAddress: "0x2000",
			OrderedAddresses:  []string{"0x3000", "0x4000"},
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

func TestControlledMovementDoesNotJoinChangedOrUnidentifiedVectors(t *testing.T) {
	start := time.Date(2026, 10, 27, 0, 0, 0, 0, time.UTC)
	a := New()
	frame := func(second int, x float64) live.OrderedClientFrame {
		at := start.Add(time.Duration(second) * time.Second)
		return live.OrderedClientFrame{ProcessID: 17, SeenUTC: at, TickMS: at.UnixMilli(),
			AreaHash: 1, CoordinateSpace: "world",
			AreaInstanceAddress: "0x1000", VectorHeaderAddress: "0x2000",
			OrderedAddresses:  []string{"0x3000", "0x4000"},
			PlayerOrderSource: "unverified_exilecore_local_slot_candidate",
			Players:           []live.OrderedLocalPlayer{{LocalPlayer: 1, X: x}, {LocalPlayer: 2}}}
	}
	first := frame(0, 0)
	changed := frame(1, 100)
	changed.OrderedAddresses = []string{"0x4000", "0x3000"}
	missing := frame(2, 200)
	missing.VectorHeaderAddress = ""
	for _, f := range []live.OrderedClientFrame{first, changed, missing} {
		if !a.ObserveFast(f) {
			t.Fatal("candidate frame rejected")
		}
	}
	report := a.ControlledMovement([]MovementWindow{{Label: "P1 only", End: 3 * time.Second}})[17][0]
	if report.CompleteFrames != 3 || report.VectorChanges != 1 ||
		report.MissingVectorIdentitySteps != 1 || report.SameAreaSteps != 0 ||
		report.PathDistanceWorld != [2]float64{} {
		t.Fatalf("changed vectors contributed to a controlled movement path: %+v", report)
	}
	fast := a.Reports()[0].FastMotion
	if fast == nil || fast.VectorChanges != 1 ||
		fast.MissingVectorIdentitySteps != 1 || fast.SlotMovementFrames != [2]int{} {
		t.Fatalf("changed vectors contributed to fast movement counts: %+v", fast)
	}
}
