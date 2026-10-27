package slotaudit

import (
	"testing"
	"time"

	"partypilot/internal/live"
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

func TestFastCandidateCorrelationStaysDiagnostic(t *testing.T) {
	now := time.Date(2026, 10, 27, 0, 0, 0, 0, time.UTC)
	a := New()
	a.Observe(telemetry.Sample{ProcessID: 17, SeenUTC: now, AreaHash: 8,
		CoordinateSpace: "world", X: 10, Y: 20, ObservedLocalAddress: "0x2",
		LocalSlotCandidates: &telemetry.LocalSlotCandidates{
			Status: "two_slot_candidate", OrderedAddresses: []string{"0x1", "0x2"}}})
	frame := func(at time.Time, area uint32, first float64) live.OrderedClientFrame {
		return live.OrderedClientFrame{ProcessID: 17, SeenUTC: at, TickMS: at.UnixMilli(),
			AreaHash: area, CoordinateSpace: "world",
			PlayerOrderSource: "unverified_exilecore_local_slot_candidate",
			Players: []live.OrderedLocalPlayer{{LocalPlayer: 1, X: first},
				{LocalPlayer: 2, X: 13, Y: 20}}}
	}
	if !a.ObserveFast(frame(now.Add(20*time.Millisecond), 8, 1)) ||
		!a.ObserveFast(frame(now.Add(40*time.Millisecond), 8, 2)) ||
		!a.ObserveFast(frame(now.Add(60*time.Millisecond), 9, 3)) {
		t.Fatal("valid candidate rejected")
	}
	trusted := frame(now, 8, 0)
	trusted.PlayerOrderReliable = true
	if a.ObserveFast(trusted) {
		t.Fatal("trusted-looking frame accepted")
	}
	r := a.Reports()[0].FastMotion
	if r == nil || r.Frames != 3 || r.CompleteFrames != 3 || r.AreaChanges != 1 ||
		r.SlotMovementFrames != [2]int{1, 0} || r.OrdinaryMatches != 2 ||
		r.UnmappedLocalFrames != 1 || r.NearestGapMax != 40*time.Millisecond ||
		r.ObservedWorldErrorMax != 3 {
		t.Fatalf("wrong fast diagnostic report: %+v", r)
	}
}
