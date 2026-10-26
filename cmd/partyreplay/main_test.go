package main

import "testing"

func TestParseFollowTargetsRejectsAmbiguousRoutes(t *testing.T) {
	targets, err := parseFollowTargets("2:3,4:1")
	if err != nil || len(targets) != 2 || targets[2] != 3 || targets[4] != 1 {
		t.Fatalf("valid target routes: %v, %v", targets, err)
	}
	for _, value := range []string{"2:3,2:1", "2:7", "2:0", "2", "x:1"} {
		if _, err := parseFollowTargets(value); err == nil {
			t.Fatalf("accepted ambiguous or invalid route %q", value)
		}
	}
}
