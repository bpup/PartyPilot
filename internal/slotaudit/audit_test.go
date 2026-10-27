package slotaudit

import (
	"testing"
	"time"

	"partypilot/internal/telemetry"
)

func TestTracksOrderAreaAndMovementWithoutVerdict(t *testing.T) {
	now := time.Date(2026, 10, 27, 0, 0, 0, 0, time.UTC)
	makeSample := func(offset int, area uint32, addresses []string, x float64) telemetry.Sample {
		seen := now.Add(time.Duration(offset) * time.Second)
		return telemetry.Sample{ProcessID: 17, SeenUTC: seen, TickMS: int64(offset+1) * 1000,
			AreaHash: area, IngameDataAddress: "0x100000", ObservedLocalAddress: "0x200000",
			LocalSlotCandidates: &telemetry.LocalSlotCandidates{Status: "two_slot_candidate",
				OrderedAddresses: addresses, RawPositionsSeenUTC: &seen,
				RawPositions: []*telemetry.RawSlotPosition{
					{WorldX: x, WorldY: 2}, {WorldX: 9, WorldY: 2}},
				ObservedLocalPositionComparison: &telemetry.RawLocalComparison{
					WorldXYDistance: 0.25, GridXYDistance: 12}},
		}
	}
	a := New()
	a.Observe(makeSample(1, 10, []string{"0x200000", "0x300000"}, 1))
	a.Observe(makeSample(2, 10, []string{"0x200000", "0x300000"}, 2))
	a.Observe(makeSample(3, 10, []string{"0x300000", "0x200000"}, 2))
	a.Observe(makeSample(4, 11, []string{"0x200000", "0x300000"}, 3))
	a.Observe(makeSample(2, 11, []string{"0x200000", "0x300000"}, 99))
	r := a.Reports()[0]
	if r.Samples != 5 || r.CompleteFrames != 4 || r.OrderChanges != 1 ||
		r.AreaChanges != 1 || r.SourceTimeRegressed != 1 || r.SlotMovementFrames != [2]int{1, 0} ||
		r.LocalWorldErrorMax != 0.25 || r.LocalGridErrorMax != 12 {
		t.Fatalf("wrong audit report: %+v", r)
	}
}
