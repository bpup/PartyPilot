package telemetry

import (
	"strings"
	"testing"
)

func TestRawItemCardsRemainUnassignedDiagnosticEvidence(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"ItemCardSeenUtc":"2026-10-26T00:00:00Z","ItemCardStatus":"ok",` +
		`"ItemCards":[{"Object":"0x60000","Item":"0x80000","Owner":"0x90000",` +
		`"Evidence":"unassigned_ui_card"}]}`
	sample, err := Decode([]byte(base))
	if err != nil || len(sample.ItemCards) != 1 || sample.ItemCards[0].Owner != "0x90000" {
		t.Fatalf("raw item card lost: %+v, %v", sample.ItemCards, err)
	}
	for _, changed := range []string{
		strings.Replace(base, `"Evidence":"unassigned_ui_card"`, `"Evidence":"pickup_ready"`, 1),
		strings.Replace(base, `"Owner":"0x90000"`, `"Owner":""`, 1),
		strings.Replace(base, `"ItemCardStatus":"ok"`, `"ItemCardStatus":"layout_unavailable"`, 1),
		strings.Replace(base, `"ItemCardSeenUtc":"2026-10-26T00:00:00Z"`, `"ItemCardSeenUtc":null`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("unverified item card was accepted: %s", changed)
		}
	}
	for _, status := range []string{"layout_pending", "layout_unavailable", "root_unavailable", "traversal_limit", "too_many_cards", "scan_failed"} {
		withoutCard := strings.Replace(base, `"ItemCardStatus":"ok"`, `"ItemCardStatus":"`+status+`"`, 1)
		withoutCard = strings.Replace(withoutCard, `"ItemCards":[{"Object":"0x60000","Item":"0x80000","Owner":"0x90000","Evidence":"unassigned_ui_card"}]`, `"ItemCards":null`, 1)
		if _, err := Decode([]byte(withoutCard)); err != nil {
			t.Fatalf("read status %s rejected: %v", status, err)
		}
	}
	joined := strings.Replace(base, `"Evidence":"unassigned_ui_card"`,
		`"Evidence":"unassigned_ui_card","OwnerMatch":{"EntityId":11,"Evidence":"pointer_equal_visible_player"},`+
			`"LootMatch":{"EntityId":23,"ItemPath":"Metadata/Items/Currency/CurrencyExalted",`+
			`"Name":"Exalted Orb","Evidence":"pointer_equal_visible_world_item"}`, 1)
	if sample, err := Decode([]byte(joined)); err != nil || len(sample.ItemCards) != 1 ||
		sample.ItemCards[0].OwnerMatch == nil || sample.ItemCards[0].OwnerMatch.EntityID != 11 ||
		sample.ItemCards[0].LootMatch == nil || sample.ItemCards[0].LootMatch.EntityID != 23 {
		t.Fatalf("exact pointer candidates lost: %+v, %v", sample.ItemCards, err)
	}
	for _, changed := range []string{
		strings.Replace(joined, `"pointer_equal_visible_player"`, `"player_1_confirmed"`, 1),
		strings.Replace(joined, `"pointer_equal_visible_world_item"`, `"pickup_ready"`, 1),
		strings.Replace(joined, `"EntityId":23`, `"EntityId":0`, 1),
		strings.Replace(joined, `"Metadata/Items/Currency/CurrencyExalted"`, `"Metadata/Monsters/Beast"`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("unverified card correlation accepted: %s", changed)
		}
	}
	withOrder := strings.Replace(joined, `"ItemCardSeenUtc"`,
		`"IngameDataAddress":"0x100000","ObservedLocalAddress":"0xA0000",`+
			`"LocalSlotCandidateStatus":"ok","LocalSlotCandidates":{"Status":"two_slot_candidate",`+
			`"Anchor":"test anchor","OffsetProfile":"unverified static offsets",`+
			`"OrderedAddresses":["0xA0000","0x90000"],"MatchingHeaders":1,`+
			`"StatePointerAreaAddress":"0x100000","StatePointerMatchesDataAddress":true,`+
			`"CandidateAreaAddresses":["0x100000"]},"ItemCardSeenUtc"`, 1)
	withOrder = strings.Replace(withOrder, `"Evidence":"unassigned_ui_card"`,
		`"Evidence":"unassigned_ui_card","SlotCandidate":{"LocalSlot":2,`+
			`"Evidence":"unverified_vector_order_candidate"}`, 1)
	if sample, err := Decode([]byte(withOrder)); err != nil || len(sample.ItemCards) != 1 ||
		sample.ItemCards[0].SlotCandidate == nil || sample.ItemCards[0].SlotCandidate.LocalSlot != 2 {
		t.Fatalf("unverified slot candidate lost: %+v, %v", sample.ItemCards, err)
	}
	for _, changed := range []string{
		strings.Replace(withOrder, `"LocalSlot":2`, `"LocalSlot":1`, 1),
		strings.Replace(withOrder, `"unverified_vector_order_candidate"`, `"verified_player_2"`, 1),
		strings.Replace(withOrder, `"Status":"two_slot_candidate"`, `"Status":"no_candidate"`, 1),
		strings.Replace(withOrder, `"Owner":"0x90000"`, `"Owner":"0xB0000"`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("unverified slot assignment accepted: %s", changed)
		}
	}
}
