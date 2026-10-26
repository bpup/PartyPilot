package telemetry

import (
	"testing"
	"time"
)

func TestMonitorReportsCandidateOnly(t *testing.T) {
	now := time.Unix(10, 0)
	monitor, ok := NewMonitor(11, 22, time.Second)
	if !ok {
		t.Fatal("invalid test monitor")
	}
	if monitor.Compare(now).Status != Missing {
		t.Fatal("missing samples not detected")
	}
	if monitor.Update(Sample{ProcessID: 33}) {
		t.Fatal("accepted another process")
	}
	monitor.Update(Sample{ProcessID: 11, SeenUTC: now, AreaName: "zone", AreaHash: 7, X: 10})
	monitor.Update(Sample{ProcessID: 22, SeenUTC: now, AreaName: "zone", AreaHash: 7})
	result := monitor.Compare(now)
	if result.Status != CandidateSameArea || result.Distance != 10 {
		t.Fatalf("unexpected comparison: %+v", result)
	}
	monitor.Update(Sample{ProcessID: 22, SeenUTC: now, AreaName: "zone", AreaHash: 8})
	if monitor.Compare(now).Status != DifferentArea {
		t.Fatal("mismatched hash accepted")
	}
	if monitor.Compare(now.Add(2*time.Second)).Status != Stale {
		t.Fatal("stale samples accepted")
	}
}

func TestDecodeRejectsMissingIdentity(t *testing.T) {
	if _, err := Decode([]byte("{}")); err == nil {
		t.Fatal("accepted missing process ID")
	}
}
