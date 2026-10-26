package telemetry

import "testing"

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
