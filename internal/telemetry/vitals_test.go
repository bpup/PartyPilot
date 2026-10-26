package telemetry

import (
	"strings"
	"testing"
)

func TestDecodeOptionalVitals(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3`
	old, err := Decode([]byte(base + `}`))
	if err != nil || old.Life != nil {
		t.Fatalf("position-only sample rejected: %+v, %v", old, err)
	}
	withVitals, err := Decode([]byte(base + `,"Life":30,"LifeMax":100,"Mana":20,"ManaMax":50,"EnergyShield":0,"EnergyShieldMax":200}`))
	if err != nil || withVitals.Life == nil || *withVitals.Life != 30 || withVitals.EnergyShieldMax == nil {
		t.Fatalf("vitals sample rejected: %+v, %v", withVitals, err)
	}
	for _, suffix := range []string{
		`,"Life":30}`,
		`,"Life":-1,"LifeMax":100}`,
		`,"Mana":20,"ManaMax":-1}`,
	} {
		if _, err := Decode([]byte(base + suffix)); err == nil {
			t.Fatalf("invalid vitals accepted: %s", suffix)
		}
	}
}

func TestVisiblePlayerDiagnosticsCannotDeclareTwoLocalEntities(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3`
	valid := base + `,"VisiblePlayersStatus":"ok","VisiblePlayers":[` +
		`{"EntityId":11,"Name":"One","X":1,"Y":2,"Z":3,"IsObservedLocal":true},` +
		`{"EntityId":12,"Name":"Two","X":4,"Y":5,"Z":6,"IsObservedLocal":false}]}`
	sample, err := Decode([]byte(valid))
	if err != nil || len(sample.VisiblePlayers) != 2 || !sample.VisiblePlayers[0].IsObservedLocal ||
		sample.VisiblePlayers[1].IsObservedLocal {
		t.Fatalf("visible entity diagnostics were not preserved: %+v, %v", sample, err)
	}
	for _, suffix := range []string{
		`,"VisiblePlayers":[{"EntityId":11,"Name":"One","X":1,"Y":2,"Z":3,"IsObservedLocal":true},` +
			`{"EntityId":12,"Name":"Two","X":4,"Y":5,"Z":6,"IsObservedLocal":true}]}`,
		`,"VisiblePlayers":[{"EntityId":11,"Name":"One","X":1,"Y":2,"Z":3},` +
			`{"EntityId":11,"Name":"Other","X":4,"Y":5,"Z":6}]}`,
		`,"VisiblePlayers":[{"EntityId":12,"Name":"","X":4,"Y":5,"Z":6}]}`,
	} {
		if _, err := Decode([]byte(base + suffix)); err == nil {
			t.Fatalf("invalid visible entity evidence accepted: %s", suffix)
		}
	}
}

func TestVisiblePlayerAddressEvidenceIsDiagnosticOnly(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"IngameDataAddress":"0x100000","ObservedLocalAddress":"0x200000",` +
		`"VisiblePlayersStatus":"ok","VisiblePlayers":[` +
		`{"EntityId":11,"Address":"0x200000","Path":"Metadata/Characters/Player","Name":"One",` +
		`"X":1,"Y":2,"Z":3,"IsObservedLocal":true},` +
		`{"EntityId":12,"Address":"0x300000","Path":"Metadata/Characters/Player","Name":"Two",` +
		`"X":4,"Y":5,"Z":6,"IsObservedLocal":false}]}`
	sample, err := Decode([]byte(base))
	if err != nil || sample.IngameDataAddress != "0x100000" || sample.ObservedLocalAddress != "0x200000" ||
		sample.VisiblePlayers[1].Address != "0x300000" {
		t.Fatalf("address diagnostic evidence was not preserved: %+v, %v", sample, err)
	}
	for _, value := range []string{
		`"ObservedLocalAddress":"0x999999"`,
		`"ObservedLocalAddress":"200000"`,
		`"ObservedLocalAddress":"0x0"`,
	} {
		changed := strings.Replace(base, `"ObservedLocalAddress":"0x200000"`, value, 1)
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("invalid local address evidence accepted: %s", value)
		}
	}
}

func TestLocalSlotCandidateDiagnosticCannotAssertUnobservedOrder(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"IngameDataAddress":"0x100000","ObservedLocalAddress":"0x200000","LocalSlotCandidateStatus":"ok",` +
		`"LocalSlotCandidates":{"Status":"two_slot_candidate",` +
		`"Anchor":"ExileCore2.IngameState.Data.Address (unverified area anchor)",` +
		`"OffsetProfile":"supplied-build static defaults (active offsets unverified)",` +
		`"OrderedAddresses":["0x200000","0x300000"],"MatchingHeaders":1,` +
		`"StatePointerAreaAddress":"0x100000","StatePointerMatchesDataAddress":true,` +
		`"CandidateAreaAddresses":["0x100000"]}}`
	got, err := Decode([]byte(base))
	if err != nil || got.LocalSlotCandidates == nil || got.LocalSlotCandidates.Status != "two_slot_candidate" {
		t.Fatalf("candidate diagnostic rejected: %+v, %v", got, err)
	}
	for _, changed := range []string{
		strings.Replace(base, `"0x200000","0x300000"`, `"0x300000","0x400000"`, 1),
		strings.Replace(base, `"0x200000","0x300000"`, `"0x200000","0x200000"`, 1),
		strings.Replace(base, `"LocalSlotCandidateStatus":"ok"`, `"LocalSlotCandidateStatus":"scan_failed"`, 1),
		strings.Replace(base, `"Status":"two_slot_candidate"`, `"Status":"reliable"`, 1),
		strings.Replace(base, `"StatePointerMatchesDataAddress":true`, `"StatePointerMatchesDataAddress":false`, 1),
		strings.Replace(base, `"CandidateAreaAddresses":["0x100000"]`, `"CandidateAreaAddresses":["0x900000"]`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("invalid candidate diagnostic accepted: %s", changed)
		}
	}
}

func TestRawLocalSlotPositionsRemainDiagnostic(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"ObservedLocalAddress":"0x200000","LocalSlotCandidateStatus":"ok",` +
		`"LocalSlotCandidates":{"Status":"two_slot_candidate","Anchor":"static scan",` +
		`"OffsetProfile":"supplied-build defaults (unverified)",` +
		`"OrderedAddresses":["0x200000","0x300000"],"MatchingHeaders":1,` +
		`"CandidateAreaAddresses":["0x100000"],"StatePointerAreaAddress":"0x100000",` +
		`"VectorHeaderAddress":"0x1005C8","RawPositionsSeenUtc":"2026-10-26T00:00:00Z","RawPositions":[` +
		`{"WorldX":108.69565,"WorldY":217.3913,"WorldZ":5,` +
		`"GridX":10,"GridY":20,"RenderComponentAddress":"0x400000"},null],` +
		`"ObservedLocalPositionComparison":{"ObservedX":10,"ObservedY":20,"ObservedZ":5,` +
		`"WorldXYDistance":220.69018,"GridXYDistance":0}}}`
	got, err := Decode([]byte(base))
	if err != nil || got.LocalSlotCandidates == nil ||
		len(got.LocalSlotCandidates.RawPositions) != 2 ||
		got.LocalSlotCandidates.RawPositions[0].GridX != 10 {
		t.Fatalf("raw diagnostic positions lost: %+v, %v", got.LocalSlotCandidates, err)
	}
	withVisible := strings.Replace(base, `"GridXYDistance":0}}}`,
		`"GridXYDistance":0},"VisiblePositionComparisons":[{"ObservedX":10,"ObservedY":20,`+
			`"ObservedZ":5,"WorldXYDistance":220.69018,"GridXYDistance":0},null]}}`, 1)
	if got, err := Decode([]byte(withVisible)); err != nil || got.LocalSlotCandidates == nil ||
		len(got.LocalSlotCandidates.VisiblePositionComparisons) != 2 ||
		got.LocalSlotCandidates.VisiblePositionComparisons[0] == nil {
		t.Fatalf("visible comparison rejected: %v", err)
	}
	for _, changed := range []string{
		strings.Replace(withVisible, `},null]}}`, `}]}}`, 1),
		strings.Replace(withVisible, `},null]}}`, `},{"ObservedX":10,"ObservedY":20,"ObservedZ":5,`+
			`"WorldXYDistance":220.69018,"GridXYDistance":0}]}}`, 1),
		strings.Replace(withVisible, `"WorldXYDistance":220.69018,"GridXYDistance":0},null]`,
			`"WorldXYDistance":1,"GridXYDistance":0},null]`, 1),
		strings.Replace(withVisible, `"GridXYDistance":0},null]`, `"GridXYDistance":-1},null]`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("invalid visible comparison accepted: %s", changed)
		}
	}
	for _, changed := range []string{
		strings.Replace(base, `"VectorHeaderAddress":"0x1005C8",`, ``, 1),
		strings.Replace(base, `"RawPositionsSeenUtc":"2026-10-26T00:00:00Z",`, ``, 1),
		strings.Replace(base, `"RawPositionsSeenUtc":"2026-10-26T00:00:00Z"`, `"RawPositionsSeenUtc":"2026-10-26T00:00:02Z"`, 1),
		strings.Replace(base, `"RenderComponentAddress":"0x400000"`, `"RenderComponentAddress":"bad"`, 1),
		strings.Replace(base, `"GridX":10`, `"GridX":1e999`, 1),
		strings.Replace(base, `},null]`, `}]`, 1),
		strings.Replace(base, `"GridXYDistance":0`, `"GridXYDistance":-1`, 1),
		strings.Replace(base, `"WorldXYDistance":220.69018`, `"WorldXYDistance":1`, 1),
		strings.Replace(base, `"0x200000","0x300000"`, `"0x300000","0x400000"`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("invalid raw diagnostic position accepted: %s", changed)
		}
	}
}
