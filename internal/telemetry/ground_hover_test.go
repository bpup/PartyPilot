package telemetry

import (
	"strings"
	"testing"
)

func TestGroundHoverIsPreservedOnlyAsUIEvidence(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"RadarSeenUtc":"2026-10-26T00:00:00Z","RadarStatus":"ok","RadarEntities":[],` +
		`"GroundHoverStatus":"ui_hover_observed","GroundHover":{"EntityId":9,` +
		`"Address":"0x1234","Path":"Metadata/MiscellaneousObjects/WorldItem@123",` +
		`"ItemPath":"Metadata/Items/Currency/CurrencyIdentification","Name":"Scroll of Wisdom",` +
		`"Evidence":"ui_hover_only"}}`
	sample, err := Decode([]byte(base))
	if err != nil || sample.GroundHover == nil || sample.GroundHover.EntityID != 9 ||
		sample.GroundHover.Evidence != "ui_hover_only" {
		t.Fatalf("UI hover evidence was not preserved: %+v, %v", sample.GroundHover, err)
	}
	for _, changed := range []string{
		strings.Replace(base, `"GroundHoverStatus":"ui_hover_observed"`, `"GroundHoverStatus":"none"`, 1),
		strings.Replace(base, `"Evidence":"ui_hover_only"`, `"Evidence":"pickup_ready"`, 1),
		strings.Replace(base, `"EntityId":9`, `"EntityId":0`, 1),
		strings.Replace(base, `"ItemPath":"Metadata/Items/Currency/CurrencyIdentification",`, "", 1),
		strings.Replace(base, `"Path":"Metadata/MiscellaneousObjects/WorldItem@123"`, `"Path":"Other/Item"`, 1),
		strings.Replace(base, `"RadarSeenUtc":"2026-10-26T00:00:00Z"`, `"RadarSeenUtc":null`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("unverified hover evidence accepted: %s", changed)
		}
	}
}

func TestGroundHoverCanReportNoObservationOrReadFailure(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"RadarSeenUtc":"2026-10-26T00:00:00Z","RadarStatus":"ok","RadarEntities":[],` +
		`"GroundHoverStatus":"none","GroundHover":null}`
	if _, err := Decode([]byte(base)); err != nil {
		t.Fatalf("no UI hover rejected: %v", err)
	}
	if _, err := Decode([]byte(strings.Replace(base, `"none"`, `"scan_failed"`, 1))); err != nil {
		t.Fatalf("read failure rejected: %v", err)
	}
}
