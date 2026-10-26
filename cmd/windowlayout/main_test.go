package main

import "testing"

func TestParseNumbers(t *testing.T) {
	if got, err := parseNumbers("1,2,3", false); err != nil || len(got) != 3 || got[2] != 3 {
		t.Fatalf("valid process list failed: %v, %v", got, err)
	}
	if got, err := parseNumbers("1,1", true); err != nil || len(got) != 2 {
		t.Fatalf("shared monitor assignment failed: %v, %v", got, err)
	}
	for _, value := range []string{"", "0", "1,1", "1,2,3,4", "a"} {
		if _, err := parseNumbers(value, false); err == nil {
			t.Fatalf("accepted invalid list %q", value)
		}
	}
}
