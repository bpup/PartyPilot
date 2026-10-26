package partylog

import "strings"

type Kind string

const (
	Disbanded  Kind = "party_disbanded"
	LocalLeft  Kind = "local_player_left"
	MemberLeft Kind = "member_left"
)

type Event struct {
	Kind       Kind
	ClientSlot int
	MemberName string
}

// Parse recognizes only the departure messages visible in the supplied build.
// A member name is evidence only after it matches a configured character name.
func Parse(line string, clientSlot int) (Event, bool) {
	text := strings.TrimSpace(line)
	if text == "" || clientSlot < 1 || clientSlot > 6 {
		return Event{}, false
	}
	lower := strings.ToLower(text)
	if strings.Contains(lower, "left the area") {
		return Event{}, false
	}
	if strings.Contains(lower, "the party has been disbanded") {
		return Event{Kind: Disbanded, ClientSlot: clientSlot}, true
	}
	if strings.Contains(lower, "you have left the party") || strings.Contains(lower, "you have been kicked from the party") {
		return Event{Kind: LocalLeft, ClientSlot: clientSlot}, true
	}
	lower = strings.TrimSuffix(lower, ".")
	text = strings.TrimSuffix(text, ".")
	for _, suffix := range []string{" has left the party", " has been kicked from the party", " was removed from the party"} {
		if !strings.HasSuffix(lower, suffix) {
			continue
		}
		prefix := strings.TrimSpace(text[:len(text)-len(suffix)])
		if index := strings.LastIndexByte(prefix, ':'); index >= 0 {
			prefix = strings.TrimSpace(prefix[index+1:])
		}
		if prefix == "" {
			return Event{}, false
		}
		return Event{Kind: MemberLeft, ClientSlot: clientSlot, MemberName: prefix}, true
	}
	return Event{}, false
}
