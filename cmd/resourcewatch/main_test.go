package main

import "testing"

func TestParsePIDs(t *testing.T) {
	pids, err := parsePIDs("123,456,789")
	if err != nil || len(pids) != 3 || pids[2] != 789 {
		t.Fatalf("valid PID selection failed: %+v %v", pids, err)
	}
	for _, value := range []string{"", "0", "123,123", "123,a", "1,2,3,4"} {
		if _, err := parsePIDs(value); err == nil {
			t.Fatalf("invalid PID selection accepted: %q", value)
		}
	}
}
