package main

import (
	"testing"
	"time"
)

func TestParseMovementWindow(t *testing.T) {
	w, err := parseMovementWindow("P1 only", "5:12.5")
	if err != nil || w.Label != "P1 only" || w.Start != 5*time.Second ||
		w.End != 12500*time.Millisecond {
		t.Fatalf("unexpected movement window: %+v, %v", w, err)
	}
	for _, input := range []string{"", "5", "5:5", "-1:2", "2:1", "a:2", "1:4000", "NaN:2", "1:+Inf"} {
		if _, err := parseMovementWindow("P1 only", input); err == nil {
			t.Fatalf("accepted invalid window %q", input)
		}
	}
}
