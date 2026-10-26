package main

import "testing"

func TestParseRosterRejectsAmbiguousAssignments(t *testing.T) {
	roster, err := parseRoster("1:101,2:102,6:106")
	if err != nil || len(roster) != 3 || roster[6] != 106 {
		t.Fatalf("valid roster: %v, %v", roster, err)
	}
	for _, value := range []string{"", "1:101,1:102", "0:101", "7:101", "2:0", "2", "2:abc"} {
		if _, err := parseRoster(value); err == nil {
			t.Fatalf("accepted invalid roster %q", value)
		}
	}
}
