package main

import (
	"testing"

	"partypilot/internal/windowlayout"
)

func TestMonitorAssignments(t *testing.T) {
	displays := []windowlayout.Display{{ID: "monitor-1"}, {ID: "monitor-2"}}
	ids, err := monitorIDs("1,2,1", displays, 3)
	if err != nil || len(ids) != 3 || ids[1] != "monitor-2" {
		t.Fatalf("valid assignments failed: %v, %v", ids, err)
	}
	for _, value := range []string{"", "1", "0,2", "1,3", "a,2"} {
		if _, err := monitorIDs(value, displays, 2); err == nil {
			t.Fatalf("accepted invalid display assignment %q", value)
		}
	}
}
