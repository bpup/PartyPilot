package telemetry

import (
	"encoding/json"
	"strings"
	"testing"
	"time"
)

func TestSkillBindCandidateSurvivesTelemetryWithoutBecomingAuthority(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:02Z","X":1,"Y":2,"Z":3,` +
		`"IngameDataAddress":"0x100000","ObservedLocalAddress":"0x200000",` +
		`"LocalSlotCandidateStatus":"ok","LocalSlotCandidates":{` +
		`"Status":"single_slot_candidate","Anchor":"static scan","OffsetProfile":"unverified",` +
		`"OrderedAddresses":["0x200000"],"MatchingHeaders":1,` +
		`"StatePointerAreaAddress":"0x100000","StatePointerMatchesDataAddress":true,` +
		`"CandidateAreaAddresses":["0x100000"],"VectorHeaderAddress":"0x1005C8"},` +
		`"SkillBindSourceStatus":"candidate_unverified_build",` +
		`"SkillBindSourceSeenUtc":"2026-10-26T00:00:01Z",` +
		`"SkillBindSource":{"Status":"candidate_unverified_build",` +
		`"PlayerDataAddress":"0x500000","MatchingRoots":1,"Entries":[` +
		`{"Slot":1,"Identity":"85000040","Kind":"gem","Button":"RT",` +
		`"ButtonConfirmed":true,"GemTile":0,"ActionIndex":0,` +
		`"ReservedAction":null,"MatchedRawSkillId":null,"MatchedSkillName":null}],` +
		`"Players":[{"LocalPlayerSlot":1,"EntityAddress":"0x200000",` +
		`"PlayerDataAddress":"0x500000","Entries":[` +
		`{"Slot":1,"Identity":"85000040","Kind":"gem","Button":"RT",` +
		`"ButtonConfirmed":true,"GemTile":0,"ActionIndex":0,` +
		`"ReservedAction":null,"MatchedRawSkillId":null,"MatchedSkillName":null}]}]}}`
	got, err := Decode([]byte(base))
	if err != nil || got.SkillBindSource == nil ||
		got.SkillBindSource.Status != "candidate_unverified_build" ||
		got.SkillBindSource.Entries[0].Kind != "gem" {
		t.Fatalf("candidate evidence lost: %+v, %v", got.SkillBindSource, err)
	}
	encoded, err := json.Marshal(got)
	if err != nil || !strings.Contains(string(encoded), `"SkillBindSource"`) {
		t.Fatalf("candidate was dropped by the telemetry path: %s, %v", encoded, err)
	}
	for _, changed := range []string{
		strings.Replace(base, `"SkillBindSourceSeenUtc":"2026-10-26T00:00:01Z"`,
			`"SkillBindSourceSeenUtc":"2026-10-26T00:00:03Z"`, 1),
		strings.Replace(base, `"0x200000"],"MatchingHeaders"`, `"0x300000"],"MatchingHeaders"`, 1),
		strings.Replace(base, `"candidate_unverified_build","SkillBindSourceSeenUtc"`,
			`"verified","SkillBindSourceSeenUtc"`, 1),
		strings.Replace(base, `"Identity":"85000040"`, `"Identity":"05000040"`, 1),
		strings.Replace(base, `"Button":"RT"`, `"Button":"A"`, 1),
		strings.Replace(base, `"GemTile":0`, `"GemTile":1`, 1),
		strings.Replace(base, `"PlayerDataAddress":"0x500000"`,
			`"PlayerDataAddress":""`, 1),
		strings.Replace(base, `"LocalPlayerSlot":1`, `"LocalPlayerSlot":2`, 1),
		strings.Replace(base, `"EntityAddress":"0x200000"`,
			`"EntityAddress":"0x300000"`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("invalid skill candidate accepted: %s", changed)
		}
	}
}

func TestSkillBindTwoPlayerOrderMapsLocalEntry(t *testing.T) {
	button := "RT"
	gemTile, action := 0, 0
	entry := SkillBindEntry{Slot: 1, Identity: "85000040", Kind: "gem", Button: &button,
		ButtonConfirmed: true, GemTile: &gemTile, ActionIndex: &action}
	ordered := &LocalSlotCandidates{Status: "two_slot_candidate",
		OrderedAddresses: []string{"0x300000", "0x200000"}}
	source := &SkillBindSource{Status: "candidate_unverified_build",
		PlayerDataAddress: "0x500000", MatchingRoots: 1, Entries: []SkillBindEntry{entry},
		Players: []SkillBindPlayer{
			{LocalPlayerSlot: 1, EntityAddress: "0x300000", PlayerDataAddress: "0x400000"},
			{LocalPlayerSlot: 2, EntityAddress: "0x200000", PlayerDataAddress: "0x500000",
				Entries: []SkillBindEntry{entry}},
		}}
	seen := time.Date(2026, 10, 26, 0, 0, 1, 0, time.UTC)
	if !validSkillBindSource(source, source.Status, &seen, ordered, "0x200000", seen.Add(time.Second)) {
		t.Fatal("second local player was not joined to the matching data object")
	}
	source.Players[1].PlayerDataAddress = "0x400000"
	if validSkillBindSource(source, source.Status, &seen, ordered, "0x200000", seen.Add(time.Second)) {
		t.Fatal("duplicate or mismatched player-data object was accepted")
	}
}

func TestSkillBindAmbiguityHasNoEntries(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:02Z","X":1,"Y":2,"Z":3,` +
		`"IngameDataAddress":"0x100000","ObservedLocalAddress":"0x200000",` +
		`"LocalSlotCandidateStatus":"ok",` +
		`"LocalSlotCandidates":{"Status":"single_slot_candidate","Anchor":"static scan",` +
		`"OffsetProfile":"unverified","OrderedAddresses":["0x200000"],` +
		`"MatchingHeaders":1,"StatePointerAreaAddress":"0x100000",` +
		`"CandidateAreaAddresses":["0x100000"]},` +
		`"SkillBindSourceStatus":"ambiguous_candidates",` +
		`"SkillBindSourceSeenUtc":"2026-10-26T00:00:01Z",` +
		`"SkillBindSource":{"Status":"ambiguous_candidates","PlayerDataAddress":null,` +
		`"MatchingRoots":2,"Entries":[],"Players":[]}}`
	if _, err := Decode([]byte(base)); err != nil {
		t.Fatalf("ambiguous source status was lost: %v", err)
	}
	for _, changed := range []string{
		strings.Replace(base, `"MatchingRoots":2`, `"MatchingRoots":1`, 1),
		strings.Replace(base, `"Entries":[]`,
			`"Entries":[{"Slot":1,"Identity":"85000040","Kind":"gem"}]`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("ambiguous source claimed a binding: %s", changed)
		}
	}
}
