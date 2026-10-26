package telemetry

import (
	"reflect"
	"strconv"
	"strings"
	"time"
)

// SkillBindSource is a read-only candidate from static 0.6.17 offsets.
// It never authorizes controller input or claims an active game build match.
type SkillBindSource struct {
	Status            string            `json:"Status"`
	PlayerDataAddress string            `json:"PlayerDataAddress"`
	MatchingRoots     int               `json:"MatchingRoots"`
	Entries           []SkillBindEntry  `json:"Entries"`
	Players           []SkillBindPlayer `json:"Players"`
}

type SkillBindPlayer struct {
	LocalPlayerSlot   int              `json:"LocalPlayerSlot"`
	EntityAddress     string           `json:"EntityAddress"`
	PlayerDataAddress string           `json:"PlayerDataAddress"`
	Entries           []SkillBindEntry `json:"Entries"`
}

type SkillBindEntry struct {
	Slot              int     `json:"Slot"`
	Identity          string  `json:"Identity"`
	Kind              string  `json:"Kind"`
	Button            *string `json:"Button"`
	ButtonConfirmed   bool    `json:"ButtonConfirmed"`
	GemTile           *int    `json:"GemTile"`
	ActionIndex       *int    `json:"ActionIndex"`
	ReservedAction    *string `json:"ReservedAction"`
	MatchedRawSkillID *string `json:"MatchedRawSkillId"`
	MatchedSkillName  *string `json:"MatchedSkillName"`
}

var skillBindButtons = [12]string{
	"RB", "RT", "LB", "A", "X", "Y", "B", "", "D-pad Left", "D-pad Up", "D-pad Right", "D-pad Down",
}

func validSkillBindSource(source *SkillBindSource, status string, seen *time.Time,
	ordered *LocalSlotCandidates, observed string, sampleSeen time.Time) bool {
	if source == nil {
		return seen == nil && (status == "" || status == "ordered_players_unverified" ||
			status == "scan_failed")
	}
	if seen == nil || seen.IsZero() || seen.After(sampleSeen) ||
		sampleSeen.Sub(*seen) > 3*time.Second || status != source.Status ||
		ordered == nil || (ordered.Status != "single_slot_candidate" &&
		ordered.Status != "two_slot_candidate") ||
		!containsAddress(ordered.OrderedAddresses, observed) ||
		source.MatchingRoots < 0 || source.MatchingRoots > 896 {
		return false
	}
	if status != "candidate_unverified_build" {
		if source.PlayerDataAddress != "" || len(source.Entries) != 0 ||
			len(source.Players) != 0 {
			return false
		}
		switch status {
		case "missing_identity", "area_unverified", "area_unreadable", "no_valid_candidate":
			return source.MatchingRoots == 0
		case "ambiguous_candidates":
			return source.MatchingRoots >= 2
		default:
			return false
		}
	}
	if source.PlayerDataAddress == "" || !validAddress(source.PlayerDataAddress) ||
		source.MatchingRoots < 1 || len(source.Entries) < 1 || len(source.Entries) > 24 ||
		len(source.Players) != len(ordered.OrderedAddresses) {
		return false
	}
	localMatched := false
	dataSeen := make(map[string]bool, len(source.Players))
	for index, player := range source.Players {
		if player.LocalPlayerSlot != index+1 ||
			!strings.EqualFold(player.EntityAddress, ordered.OrderedAddresses[index]) ||
			player.PlayerDataAddress == "" || !validAddress(player.PlayerDataAddress) ||
			dataSeen[player.PlayerDataAddress] || len(player.Entries) > 24 ||
			!validSkillBindEntries(player.Entries) {
			return false
		}
		dataSeen[player.PlayerDataAddress] = true
		if strings.EqualFold(player.EntityAddress, observed) {
			if player.PlayerDataAddress != source.PlayerDataAddress ||
				!reflect.DeepEqual(player.Entries, source.Entries) {
				return false
			}
			localMatched = true
		}
	}
	return localMatched && validSkillBindEntries(source.Entries)
}

func validSkillBindEntries(entries []SkillBindEntry) bool {
	previous := -1
	for _, entry := range entries {
		if entry.Slot <= previous || !validSkillBindEntry(entry) {
			return false
		}
		previous = entry.Slot
	}
	return true
}

func validSkillBindEntry(entry SkillBindEntry) bool {
	if entry.Slot < 0 || entry.Slot >= 24 || len(entry.Identity) != 8 ||
		strings.ToUpper(entry.Identity) != entry.Identity {
		return false
	}
	id, err := strconv.ParseUint(entry.Identity, 16, 32)
	if err != nil || id&0x80000000 == 0 {
		return false
	}
	base := entry.Slot % 12
	button := skillBindButtons[base]
	if entry.Slot >= 12 && button != "" {
		button = "LT+" + button
	}
	if button == "" {
		if entry.Button != nil || entry.ButtonConfirmed {
			return false
		}
	} else if entry.Button == nil || *entry.Button != button ||
		entry.ButtonConfirmed != (base <= 6) {
		return false
	}
	if entry.MatchedRawSkillID != nil &&
		(strings.TrimSpace(*entry.MatchedRawSkillID) == "" ||
			len(*entry.MatchedRawSkillID) > 128) ||
		entry.MatchedSkillName != nil && len(*entry.MatchedSkillName) > 128 ||
		(entry.MatchedRawSkillID == nil) != (entry.MatchedSkillName == nil) {
		return false
	}
	switch {
	case id>>24 == 133:
		return entry.Kind == "gem" && entry.GemTile != nil &&
			*entry.GemTile == int((id>>16)&0xff) && entry.ActionIndex != nil &&
			*entry.ActionIndex == int((id>>12)&0xf) && entry.ReservedAction == nil
	case id>>16 == 32776:
		if entry.Kind != "inherent_action" || entry.GemTile != nil ||
			entry.ActionIndex != nil || entry.MatchedRawSkillID != nil {
			return false
		}
		want := ""
		switch (id >> 8) & 0xff {
		case 14:
			want = "Interact"
		case 4:
			want = "Dodge Roll"
		case 6:
			want = "Town Portal"
		case 13, 27:
			want = "Weapon Swap"
		}
		return want == "" && entry.ReservedAction == nil ||
			want != "" && entry.ReservedAction != nil && *entry.ReservedAction == want
	case ((id >> 16) & 0x7f) == 64:
		return entry.Kind == "weapon_granted" && entry.GemTile == nil &&
			entry.ActionIndex == nil && entry.ReservedAction == nil &&
			entry.MatchedRawSkillID == nil
	default:
		return entry.Kind == "unknown" && entry.GemTile == nil &&
			entry.ActionIndex == nil && entry.ReservedAction == nil &&
			entry.MatchedRawSkillID == nil
	}
}

func containsAddress(addresses []string, observed string) bool {
	for _, address := range addresses {
		if strings.EqualFold(address, observed) {
			return true
		}
	}
	return false
}
