package main

import (
	"strings"
	"testing"
	"time"
)

func TestOnlyUnverifiedCandidatePacketsAreRecorded(t *testing.T) {
	now := time.Now().UTC()
	packet := `{"Kind":"ordered_client","ProcessId":101,"SeenUtc":"2026-10-27T00:00:00Z",` +
		`"TickMs":123,"AreaHash":42,"CoordinateSpace":"world",` +
		`"PlayerOrderReliable":false,"PlayerOrderSource":"unverified_exilecore_local_slot_candidate",` +
		`"Players":[{"LocalPlayer":1,"X":1,"Y":2,"Z":3},` +
		`{"LocalPlayer":2,"X":4,"Y":5,"Z":6}]}`
	if !validCandidate([]byte(packet), now) ||
		validCandidate([]byte(strings.Replace(packet, `"PlayerOrderReliable":false`,
			`"PlayerOrderReliable":true`, 1)), now) ||
		validCandidate([]byte(strings.Replace(packet, candidateSource, "other", 1)), now) ||
		validCandidate([]byte(`{"Kind":"ordered_client","LocalSlotCandidates":{}}`), now) {
		t.Fatal("fast logger accepted a trusted-looking or malformed packet")
	}
}
