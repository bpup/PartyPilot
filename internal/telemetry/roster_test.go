package telemetry

import (
	"testing"
	"time"
)

func TestRosterMonitorSeparatesSixPlayersByConfiguredProcess(t *testing.T) {
	now := time.Unix(100, 0).UTC()
	monitor, ok := NewRosterMonitor(map[int]int{1: 101, 2: 102, 3: 103, 4: 104, 5: 105, 6: 106}, 1, time.Second)
	if !ok {
		t.Fatal("invalid six-player roster")
	}
	if monitor.Update(Sample{ProcessID: 999, SeenUTC: now}) {
		t.Fatal("accepted unassigned process")
	}
	for slot := 1; slot <= 6; slot++ {
		hash := uint32(7)
		if slot == 4 {
			hash = 8
		}
		if slot == 6 {
			continue
		}
		if !monitor.Update(Sample{ProcessID: 100 + slot, SeenUTC: now,
			AreaName: "zone", AreaHash: hash, X: float64(slot)}) {
			t.Fatalf("rejected player %d", slot)
		}
	}
	snapshot := monitor.Snapshot(now)
	if !snapshot.Leader.Present || !snapshot.Leader.Fresh || len(snapshot.Followers) != 5 {
		t.Fatalf("leader or follower list: %+v", snapshot)
	}
	want := []Status{CandidateSameArea, CandidateSameArea, DifferentArea, CandidateSameArea, Missing}
	for index, follower := range snapshot.Followers {
		if follower.Slot != index+2 || follower.Comparison.Status != want[index] {
			t.Fatalf("player %d: %+v", index+2, follower)
		}
	}
	if snapshot.Followers[0].Comparison.Distance != 1 {
		t.Fatalf("wrong candidate distance: %+v", snapshot.Followers[0])
	}
	if monitor.Update(Sample{ProcessID: 102, SeenUTC: now.Add(-time.Second), AreaName: "wrong", AreaHash: 9}) {
		t.Fatal("accepted an older sample over a fresh one")
	}
	if monitor.Snapshot(now).Followers[0].Comparison.Status != CandidateSameArea {
		t.Fatal("older sample replaced current state")
	}
	if monitor.Snapshot(now.Add(2 * time.Second)).Followers[0].Comparison.Status != Stale {
		t.Fatal("fresh candidates did not expire")
	}
}

func TestRosterMonitorValidatesIdentityAndSupportsSolo(t *testing.T) {
	for _, roster := range []map[int]int{{1: 11, 2: 11}, {0: 11}, {7: 11}, {1: 0}} {
		if _, ok := NewRosterMonitor(roster, 1, time.Second); ok {
			t.Fatalf("accepted invalid roster: %+v", roster)
		}
	}
	now := time.Unix(101, 0).UTC()
	monitor, ok := NewRosterMonitor(map[int]int{1: 11}, 1, time.Second)
	if !ok || len(monitor.Snapshot(now).Followers) != 0 || monitor.Snapshot(now).Leader.Present {
		t.Fatal("solo roster did not start missing")
	}
	monitor.Update(Sample{ProcessID: 11, SeenUTC: now, AreaName: "zone", AreaHash: 7})
	if !monitor.Snapshot(now).Leader.Fresh || monitor.Snapshot(now.Add(2*time.Second)).Leader.Fresh {
		t.Fatal("solo leader freshness incorrect")
	}
}
