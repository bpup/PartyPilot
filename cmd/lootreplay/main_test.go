package main

import (
	"bytes"
	"strings"
	"testing"
)

func TestReplayRequiresControllerCardAndCompleteSafety(t *testing.T) {
	const prefix = `"now":"2026-10-26T00:00:00Z","player":1,"frame_sequence":1,`
	const card = `"card":{"selected_item":"0xABC","base_id":"CurrencyExalted",` +
		`"player":1,"targetable":true,"received_at":"2026-10-26T00:00:00Z"},`
	const ready = `"conditions":{"catalog_approved":true,"capacity_allows":true,` +
		`"inventory_limit_allows":true,"menu_clear":true,"own_inventory_closed":true,` +
		`"inventory_automation_free":true,"alive":true}`
	for _, test := range []struct {
		input string
		pulse bool
	}{
		{`{` + prefix + card + ready + `}`, true},
		{`{` + prefix + `"ground_hover":{"entity_id":9},` + ready + `}`, false},
		{`{` + prefix + card + `"conditions":{"catalog_approved":true}}`, false},
	} {
		var output bytes.Buffer
		err := replay(strings.NewReader(test.input), &output)
		if strings.Contains(test.input, "ground_hover") {
			if err == nil {
				t.Fatal("UI hover field was accepted as a controller card")
			}
			continue
		}
		if err != nil || strings.Contains(output.String(), `"pulse_active":true`) != test.pulse {
			t.Fatalf("unexpected replay output %q, error %v", output.String(), err)
		}
	}
}
