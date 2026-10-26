package telemetry

import (
	"fmt"
	"strings"
	"testing"
)

func TestRadarEvidenceIsBoundedAndDiagnostic(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"RadarSeenUtc":"2026-10-26T00:00:00Z","RadarStatus":"ok",` +
		`"RadarEntities":[{"EntityId":7,"Category":"monster","Path":"Metadata/Monsters/Beast",` +
		`"X":4,"Y":5,"Z":6}]}`
	sample, err := Decode([]byte(base))
	if err != nil || len(sample.RadarEntities) != 1 || sample.RadarEntities[0].Category != "monster" {
		t.Fatalf("radar evidence was not preserved: %+v, %v", sample, err)
	}
	for _, changed := range []string{
		strings.Replace(base, `"Category":"monster"`, `"Category":"boss"`, 1),
		strings.Replace(base, `"Path":"Metadata/Monsters/Beast"`, `"Path":"Random/Beast"`, 1),
		strings.Replace(base, `"RadarStatus":"ok"`, `"RadarStatus":"scan_failed"`, 1),
		strings.Replace(base, `"RadarSeenUtc":"2026-10-26T00:00:00Z"`, `"RadarSeenUtc":null`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("invalid radar evidence accepted: %s", changed)
		}
	}
}

func TestRadarPacketCanCarryNinetySixMarkers(t *testing.T) {
	makeSample := func(count int) []byte {
		var b strings.Builder
		b.WriteString(`{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,`)
		b.WriteString(`"RadarSeenUtc":"2026-10-26T00:00:00Z","RadarStatus":"ok","RadarEntities":[`)
		for index := 1; index <= count; index++ {
			if index > 1 {
				b.WriteByte(',')
			}
			fmt.Fprintf(&b, `{"EntityId":%d,"Category":"monster","Path":"Metadata/Monsters/Beast",`+
				`"X":4,"Y":5,"Z":6}`, index)
		}
		b.WriteString(`]}`)
		return []byte(b.String())
	}
	full := makeSample(96)
	if len(full) <= 4096 {
		t.Fatal("test packet did not exceed the former receiver buffer")
	}
	got, err := Decode(full)
	if err != nil || len(got.RadarEntities) != 96 {
		t.Fatalf("full radar packet rejected: count=%d, err=%v", len(got.RadarEntities), err)
	}
	if _, err := Decode(makeSample(97)); err == nil {
		t.Fatal("radar packet beyond the entity limit was accepted")
	}
}

func TestRadarPlayerRequiresObservedNameAndCharacterPath(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"RadarSeenUtc":"2026-10-26T00:00:00Z","RadarStatus":"ok",` +
		`"RadarEntities":[{"EntityId":8,"Category":"player","Path":"Metadata/Characters/Player",` +
		`"Label":"Teammate","X":4,"Y":5,"Z":6}]}`
	if sample, err := Decode([]byte(base)); err != nil || sample.RadarEntities[0].Label == nil ||
		*sample.RadarEntities[0].Label != "Teammate" {
		t.Fatalf("visible player observation rejected: %+v, %v", sample, err)
	}
	for _, changed := range []string{
		strings.Replace(base, `"Label":"Teammate"`, `"Label":null`, 1),
		strings.Replace(base, `"Label":"Teammate"`, `"Label":" "`, 1),
		strings.Replace(base, `"Path":"Metadata/Characters/Player"`, `"Path":"Metadata/Monsters/Player"`, 1),
		strings.Replace(base, `"Category":"player"`, `"Category":"monster"`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("unverified player marker accepted: %s", changed)
		}
	}
}

func TestRadarLootRequiresResolvedWorldItem(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"RadarSeenUtc":"2026-10-26T00:00:00Z","RadarStatus":"ok",` +
		`"RadarEntities":[{"EntityId":9,"Category":"loot",` +
		`"Path":"Metadata/MiscellaneousObjects/WorldItem@123",` +
		`"ItemPath":"Metadata/Items/Currency/CurrencyIdentification",` +
		`"Label":"Scroll of Wisdom","X":4,"Y":5,"Z":6}]}`
	if sample, err := Decode([]byte(base)); err != nil || sample.RadarEntities[0].ItemPath == nil ||
		*sample.RadarEntities[0].ItemPath != "Metadata/Items/Currency/CurrencyIdentification" {
		t.Fatalf("resolved loot observation rejected: %+v, %v", sample, err)
	}
	for _, changed := range []string{
		strings.Replace(base, `"ItemPath":"Metadata/Items/Currency/CurrencyIdentification"`, `"ItemPath":null`, 1),
		strings.Replace(base, `"ItemPath":"Metadata/Items/Currency/CurrencyIdentification"`, `"ItemPath":"Metadata/Monsters/Beast"`, 1),
		strings.Replace(base, `"Label":"Scroll of Wisdom"`, `"Label":null`, 1),
		strings.Replace(base, `"Path":"Metadata/MiscellaneousObjects/WorldItem@123"`, `"Path":"Metadata/MiscellaneousObjects/Chest"`, 1),
		strings.Replace(base, `"Category":"loot"`, `"Category":"chest"`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("unresolved loot marker accepted: %s", changed)
		}
	}
}
