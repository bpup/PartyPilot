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
