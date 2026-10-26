package telemetry

import (
	"encoding/json"
	"errors"
	"math"
	"strconv"
	"strings"
	"time"
)

type Sample struct {
	ProcessID                int                  `json:"ProcessId"`
	SeenUTC                  time.Time            `json:"SeenUtc"`
	AreaName                 string               `json:"AreaName"`
	AreaHash                 uint32               `json:"AreaHash"`
	X                        float64              `json:"X"`
	Y                        float64              `json:"Y"`
	Z                        float64              `json:"Z"`
	Life                     *float64             `json:"Life,omitempty"`
	LifeMax                  *float64             `json:"LifeMax,omitempty"`
	Mana                     *float64             `json:"Mana,omitempty"`
	ManaMax                  *float64             `json:"ManaMax,omitempty"`
	EnergyShield             *float64             `json:"EnergyShield,omitempty"`
	EnergyShieldMax          *float64             `json:"EnergyShieldMax,omitempty"`
	IngameDataAddress        string               `json:"IngameDataAddress,omitempty"`
	ObservedLocalAddress     string               `json:"ObservedLocalAddress,omitempty"`
	LocalSlotCandidates      *LocalSlotCandidates `json:"LocalSlotCandidates,omitempty"`
	LocalSlotCandidateStatus string               `json:"LocalSlotCandidateStatus,omitempty"`
	RadarEntities            []RadarEntity        `json:"RadarEntities,omitempty"`
	RadarStatus              string               `json:"RadarStatus,omitempty"`
	RadarSeenUTC             *time.Time           `json:"RadarSeenUtc,omitempty"`
	GroundHover              *GroundHover         `json:"GroundHover,omitempty"`
	GroundHoverStatus        string               `json:"GroundHoverStatus,omitempty"`
	ItemCards                []RawItemCard        `json:"ItemCards,omitempty"`
	ItemCardStatus           string               `json:"ItemCardStatus,omitempty"`
	ItemCardSeenUTC          *time.Time           `json:"ItemCardSeenUtc,omitempty"`
	VisiblePlayers           []VisiblePlayer      `json:"VisiblePlayers,omitempty"`
	VisiblePlayersStatus     string               `json:"VisiblePlayersStatus,omitempty"`
}

// RawItemCard is a currently attached UI node with an item and owner pointer.
// It has no validated local player, item identity or pickup authorization.
type RawItemCard struct {
	Object     string          `json:"Object"`
	Item       string          `json:"Item"`
	Owner      string          `json:"Owner"`
	Evidence   string          `json:"Evidence"`
	OwnerMatch *CardOwnerMatch `json:"OwnerMatch,omitempty"`
	LootMatch  *CardLootMatch  `json:"LootMatch,omitempty"`
}

type CardOwnerMatch struct {
	EntityID uint64 `json:"EntityId"`
	Evidence string `json:"Evidence"`
}

type CardLootMatch struct {
	EntityID uint64 `json:"EntityId"`
	ItemPath string `json:"ItemPath"`
	Name     string `json:"Name"`
	Evidence string `json:"Evidence"`
}

// GroundHover records only the entity under a UI ground-label hover. It is
// not evidence of a controller-selected card, player ownership, or pickup safety.
type GroundHover struct {
	EntityID uint64  `json:"EntityId"`
	Address  string  `json:"Address,omitempty"`
	Path     string  `json:"Path"`
	ItemPath *string `json:"ItemPath,omitempty"`
	Name     *string `json:"Name,omitempty"`
	Evidence string  `json:"Evidence"`
}

// VisiblePlayer is diagnostic evidence only. It does not establish a local
// co-op slot or authorize controller movement.
type VisiblePlayer struct {
	EntityID        uint64  `json:"EntityId"`
	Address         string  `json:"Address,omitempty"`
	Path            string  `json:"Path,omitempty"`
	Name            string  `json:"Name"`
	X               float64 `json:"X"`
	Y               float64 `json:"Y"`
	Z               float64 `json:"Z"`
	IsObservedLocal bool    `json:"IsObservedLocal"`
}

// RadarEntity is a path-and-position observation; category does not assert
// hostility, loot value, quest state, allocation, or interaction availability.
type RadarEntity struct {
	EntityID uint64  `json:"EntityId"`
	Category string  `json:"Category"`
	Path     string  `json:"Path"`
	Label    *string `json:"Label,omitempty"`
	ItemPath *string `json:"ItemPath,omitempty"`
	Stack    *int    `json:"Stack,omitempty"`
	Rarity   *string `json:"Rarity,omitempty"`
	X        float64 `json:"X"`
	Y        float64 `json:"Y"`
	Z        float64 `json:"Z"`
}

// LocalSlotCandidates are read-only hints from an unverified memory anchor.
// They must never be used as a controller or party slot assignment.
type LocalSlotCandidates struct {
	Status                         string   `json:"Status"`
	Anchor                         string   `json:"Anchor"`
	OffsetProfile                  string   `json:"OffsetProfile"`
	OrderedAddresses               []string `json:"OrderedAddresses"`
	MatchingHeaders                int      `json:"MatchingHeaders"`
	StatePointerAreaAddress        string   `json:"StatePointerAreaAddress,omitempty"`
	StatePointerMatchesDataAddress *bool    `json:"StatePointerMatchesDataAddress,omitempty"`
	CandidateAreaAddresses         []string `json:"CandidateAreaAddresses"`
}

var ErrInvalidSample = errors.New("invalid telemetry sample")

func Decode(data []byte) (Sample, error) {
	var item Sample
	if err := json.Unmarshal(data, &item); err != nil {
		return Sample{}, err
	}
	if item.ProcessID <= 0 || item.SeenUTC.IsZero() ||
		!finite(item.X) || !finite(item.Y) || !finite(item.Z) ||
		!validPool(item.Life, item.LifeMax) || !validPool(item.Mana, item.ManaMax) ||
		!validPool(item.EnergyShield, item.EnergyShieldMax) ||
		!validAddress(item.IngameDataAddress) || !validAddress(item.ObservedLocalAddress) ||
		!validLocalSlotCandidates(item.LocalSlotCandidates, item.LocalSlotCandidateStatus,
			item.ObservedLocalAddress, item.IngameDataAddress) ||
		!validRadar(item.RadarEntities, item.RadarStatus, item.RadarSeenUTC) ||
		!validGroundHover(item.GroundHover, item.GroundHoverStatus, item.RadarSeenUTC) ||
		!validRawItemCards(item.ItemCards, item.ItemCardStatus, item.ItemCardSeenUTC) ||
		!validVisiblePlayers(item.VisiblePlayers, item.ObservedLocalAddress) {
		return Sample{}, ErrInvalidSample
	}
	return item, nil
}

func validRawItemCards(cards []RawItemCard, status string, seen *time.Time) bool {
	if status == "" {
		return cards == nil && seen == nil
	}
	if seen == nil || seen.IsZero() {
		return false
	}
	if status != "ok" {
		switch status {
		case "layout_pending", "layout_unavailable", "root_unavailable", "traversal_limit", "too_many_cards", "scan_failed":
			return cards == nil
		default:
			return false
		}
	}
	if cards == nil || len(cards) > 32 {
		return false
	}
	objects := make(map[string]bool, len(cards))
	for _, card := range cards {
		if card.Object == "" || card.Item == "" || card.Owner == "" ||
			!validAddress(card.Object) || !validAddress(card.Item) || !validAddress(card.Owner) ||
			objects[card.Object] || card.Evidence != "unassigned_ui_card" {
			return false
		}
		if card.OwnerMatch != nil && (card.OwnerMatch.EntityID == 0 ||
			card.OwnerMatch.Evidence != "pointer_equal_visible_player") {
			return false
		}
		if card.LootMatch != nil && (card.LootMatch.EntityID == 0 ||
			card.LootMatch.Evidence != "pointer_equal_visible_world_item" ||
			!strings.HasPrefix(card.LootMatch.ItemPath, "Metadata/Items/") ||
			len(card.LootMatch.ItemPath) > 256 || strings.TrimSpace(card.LootMatch.Name) == "" ||
			len(card.LootMatch.Name) > 96) {
			return false
		}
		objects[card.Object] = true
	}
	return true
}

func validGroundHover(hover *GroundHover, status string, seen *time.Time) bool {
	if status == "" {
		return hover == nil
	}
	if seen == nil || seen.IsZero() {
		return false
	}
	if status == "none" || status == "scan_failed" {
		return hover == nil
	}
	if status != "ui_hover_observed" || hover == nil || hover.EntityID == 0 ||
		hover.Evidence != "ui_hover_only" || !validAddress(hover.Address) ||
		!strings.HasPrefix(hover.Path, "Metadata/") || len(hover.Path) > 256 ||
		(hover.ItemPath == nil) != (hover.Name == nil) {
		return false
	}
	if hover.ItemPath != nil && (!strings.HasPrefix(*hover.ItemPath, "Metadata/Items/") ||
		len(*hover.ItemPath) > 256 || strings.TrimSpace(*hover.Name) == "" || len(*hover.Name) > 96) {
		return false
	}
	return true
}

func validRadar(entities []RadarEntity, status string, seen *time.Time) bool {
	if status == "" {
		return seen == nil && entities == nil
	}
	if seen == nil || seen.IsZero() || (status != "ok" && status != "scan_failed") {
		return false
	}
	if status == "scan_failed" {
		return entities == nil
	}
	if entities == nil {
		return false
	}
	if len(entities) > 96 {
		return false
	}
	ids := make(map[uint64]bool, len(entities))
	for _, entity := range entities {
		if entity.EntityID == 0 || ids[entity.EntityID] || len(entity.Path) > 256 ||
			!strings.HasPrefix(entity.Path, "Metadata/") ||
			!validRadarCategory(entity.Category) ||
			!finite(entity.X) || !finite(entity.Y) || !finite(entity.Z) {
			return false
		}
		if entity.Category == "player" {
			if !strings.HasPrefix(entity.Path, "Metadata/Characters/") || entity.Label == nil ||
				strings.TrimSpace(*entity.Label) == "" || len(*entity.Label) > 64 || entity.ItemPath != nil ||
				entity.Stack != nil || entity.Rarity != nil {
				return false
			}
		} else if entity.Category == "loot" {
			if !worldItemWrapperPath(entity.Path) || entity.ItemPath == nil ||
				!strings.HasPrefix(*entity.ItemPath, "Metadata/Items/") || len(*entity.ItemPath) > 256 ||
				entity.Label == nil || strings.TrimSpace(*entity.Label) == "" || len(*entity.Label) > 96 {
				return false
			}
			if entity.Stack != nil && (*entity.Stack < 1 || *entity.Stack > 999999) {
				return false
			}
			if entity.Rarity != nil && !validLootRarity(*entity.Rarity) {
				return false
			}
		} else if entity.Label != nil || entity.ItemPath != nil || entity.Stack != nil || entity.Rarity != nil {
			return false
		}
		ids[entity.EntityID] = true
	}
	return true
}

func validLootRarity(rarity string) bool {
	switch rarity {
	case "normal", "magic", "rare", "unique":
		return true
	default:
		return false
	}
}

func worldItemWrapperPath(path string) bool {
	trimmed := strings.TrimRight(path, "/")
	leaf := trimmed[strings.LastIndexByte(trimmed, '/')+1:]
	if index := strings.IndexByte(leaf, '@'); index >= 0 {
		leaf = leaf[:index]
	}
	return strings.EqualFold(leaf, "WorldItem")
}

func validRadarCategory(category string) bool {
	switch category {
	case "monster", "checkpoint", "waypoint", "portal", "transition", "npc", "chest", "shrine", "player", "league", "loot":
		return true
	default:
		return false
	}
}

func validLocalSlotCandidates(candidate *LocalSlotCandidates, scanStatus, observed, dataAddress string) bool {
	if scanStatus != "" && scanStatus != "ok" && scanStatus != "scan_failed" {
		return false
	}
	if candidate == nil {
		return scanStatus != "ok"
	}
	if scanStatus != "ok" || candidate.Anchor == "" || candidate.OffsetProfile == "" ||
		candidate.MatchingHeaders < 0 || !validAddress(candidate.StatePointerAreaAddress) {
		return false
	}
	if candidate.StatePointerMatchesDataAddress != nil {
		if candidate.StatePointerAreaAddress == "" || dataAddress == "" ||
			*candidate.StatePointerMatchesDataAddress != (candidate.StatePointerAreaAddress == dataAddress) {
			return false
		}
	}
	want := 0
	switch candidate.Status {
	case "missing_anchor_or_local", "no_candidate", "conflicting_candidates":
		if candidate.Status == "no_candidate" || candidate.Status == "missing_anchor_or_local" {
			if candidate.MatchingHeaders != 0 {
				return false
			}
		} else if candidate.MatchingHeaders < 2 {
			return false
		}
	case "single_slot_candidate":
		want = 1
	case "two_slot_candidate":
		want = 2
	default:
		return false
	}
	if len(candidate.OrderedAddresses) != want || (want > 0 && candidate.MatchingHeaders < 1) {
		return false
	}
	if (candidate.MatchingHeaders == 0 && len(candidate.CandidateAreaAddresses) != 0) ||
		(candidate.MatchingHeaders > 0 && len(candidate.CandidateAreaAddresses) == 0) ||
		len(candidate.CandidateAreaAddresses) > 2 {
		return false
	}
	seenAreas := make(map[string]bool, len(candidate.CandidateAreaAddresses))
	for _, area := range candidate.CandidateAreaAddresses {
		if area == "" || !validAddress(area) || seenAreas[area] ||
			(area != dataAddress && area != candidate.StatePointerAreaAddress) {
			return false
		}
		seenAreas[area] = true
	}
	seen := make(map[string]bool, want)
	localSeen := false
	for _, address := range candidate.OrderedAddresses {
		if !validAddress(address) || address == "" || seen[address] {
			return false
		}
		seen[address] = true
		localSeen = localSeen || address == observed
	}
	return want == 0 || observed == "" || localSeen
}

func validVisiblePlayers(players []VisiblePlayer, observedLocalAddress string) bool {
	if len(players) > 16 {
		return false
	}
	ids := make(map[uint64]bool, len(players))
	localCount := 0
	for _, player := range players {
		if player.EntityID == 0 || player.Name == "" || ids[player.EntityID] ||
			!validAddress(player.Address) ||
			!finite(player.X) || !finite(player.Y) || !finite(player.Z) {
			return false
		}
		ids[player.EntityID] = true
		if player.IsObservedLocal {
			if player.Address != "" && observedLocalAddress != "" && player.Address != observedLocalAddress {
				return false
			}
			localCount++
		}
	}
	return localCount <= 1
}

func validAddress(value string) bool {
	if value == "" {
		return true
	}
	if !strings.HasPrefix(value, "0x") || len(value) < 4 || len(value) > 18 {
		return false
	}
	parsed, err := strconv.ParseUint(value[2:], 16, 64)
	return err == nil && parsed > 0 && parsed <= 0x7fffffffffffffff
}

func validPool(current, maximum *float64) bool {
	if current == nil && maximum == nil {
		return true
	}
	return current != nil && maximum != nil && finite(*current) && finite(*maximum) &&
		*current >= 0 && *maximum >= 0
}

func finite(value float64) bool {
	return !math.IsNaN(value) && !math.IsInf(value, 0)
}
