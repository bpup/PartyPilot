package partylog

import "testing"

func TestDepartureParsing(t *testing.T) {
	cases := []struct {
		line string
		kind Kind
		name string
	}{
		{"The party has been disbanded.", Disbanded, ""},
		{"You have left the party.", LocalLeft, ""},
		{"You have been kicked from the party.", LocalLeft, ""},
		{"2026 INFO: Leader has left the party.", MemberLeft, "Leader"},
		{"2026 INFO: Leader has been kicked from the party", MemberLeft, "Leader"},
		{"2026 INFO: Leader was removed from the party", MemberLeft, "Leader"},
	}
	for _, item := range cases {
		event, ok := Parse(item.line, 2)
		if !ok || event.Kind != item.kind || event.MemberName != item.name || event.ClientSlot != 2 {
			t.Fatalf("bad event for %q: %+v, %t", item.line, event, ok)
		}
	}
	for _, line := range []string{"", "You have left the area", "Leader has left the area", "Someone joined the party"} {
		if event, ok := Parse(line, 1); ok {
			t.Fatalf("unexpected departure for %q: %+v", line, event)
		}
	}
}
