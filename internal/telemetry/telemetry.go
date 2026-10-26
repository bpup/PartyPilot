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
	CoordinateSpace          string               `json:"CoordinateSpace,omitempty"`
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
	TerrainSummary           *RawTerrainSummary   `json:"TerrainSummary,omitempty"`
	TerrainStatus            string               `json:"TerrainStatus,omitempty"`
	TerrainSeenUTC           *time.Time           `json:"TerrainSeenUtc,omitempty"`
	PackedTerrainProbe       *PackedTerrainProbe  `json:"PackedTerrainProbe,omitempty"`
	PackedTerrainStatus      string               `json:"PackedTerrainStatus,omitempty"`
	VisiblePlayers           []VisiblePlayer      `json:"VisiblePlayers,omitempty"`
	VisiblePlayersStatus     string               `json:"VisiblePlayersStatus,omitempty"`
}

// RawTerrainSummary is diagnostic evidence only. Values are not walkability flags.
type RawTerrainSummary struct {
	Rows            int               `json:"Rows"`
	MinColumns      int               `json:"MinColumns"`
	MaxColumns      int               `json:"MaxColumns"`
	SampledCells    int               `json:"SampledCells"`
	SampleTruncated bool              `json:"SampleTruncated"`
	ValuesTruncated bool              `json:"ValuesTruncated"`
	Values          []RawTerrainValue `json:"Values"`
}

type RawTerrainValue struct {
	Value int `json:"Value"`
	Count int `json:"Count"`
}

// PackedTerrainProbe is a bounded read-only patch around a candidate player
// grid position. It is not accepted as live navigation input.
type PackedTerrainProbe struct {
	AreaAddress    string   `json:"AreaAddress"`
	Anchor         string   `json:"Anchor"`
	Width          int      `json:"Width"`
	Height         int      `json:"Height"`
	BytesPerRow    int      `json:"BytesPerRow"`
	PatchX         int      `json:"PatchX"`
	PatchY         int      `json:"PatchY"`
	PatchRows      []string `json:"PatchRows"`
	SampledNibbles int      `json:"SampledNibbles"`
	InRangeNibbles int      `json:"InRangeNibbles"`
}

// RawItemCard is a currently attached UI node with an item and owner pointer.
// It has no validated local player, item identity or pickup authorization.
type RawItemCard struct {
	Object        string             `json:"Object"`
	Item          string             `json:"Item"`
	Owner         string             `json:"Owner"`
	Evidence      string             `json:"Evidence"`
	OwnerMatch    *CardOwnerMatch    `json:"OwnerMatch,omitempty"`
	LootMatch     *CardLootMatch     `json:"LootMatch,omitempty"`
	SlotCandidate *CardSlotCandidate `json:"SlotCandidate,omitempty"`
}

type CardOwnerMatch struct {
	EntityID uint64 `json:"EntityId"`
	Evidence string `json:"Evidence"`
}

type CardLootMatch struct {
	EntityID        uint64 `json:"EntityId"`
	ItemPath        string `json:"ItemPath"`
	BaseIDCandidate string `json:"BaseIdCandidate"`
	Name            string `json:"Name"`
	Evidence        string `json:"Evidence"`
}

type CardSlotCandidate struct {
	LocalSlot int    `json:"LocalSlot"`
	Evidence  string `json:"Evidence"`
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
	Status                          string              `json:"Status"`
	Anchor                          string              `json:"Anchor"`
	OffsetProfile                   string              `json:"OffsetProfile"`
	OrderedAddresses                []string            `json:"OrderedAddresses"`
	MatchingHeaders                 int                 `json:"MatchingHeaders"`
	StatePointerAreaAddress         string              `json:"StatePointerAreaAddress,omitempty"`
	StatePointerMatchesDataAddress  *bool               `json:"StatePointerMatchesDataAddress,omitempty"`
	CandidateAreaAddresses          []string            `json:"CandidateAreaAddresses"`
	VectorHeaderAddress             string              `json:"VectorHeaderAddress,omitempty"`
	RawPositions                    []*RawSlotPosition  `json:"RawPositions,omitempty"`
	RawPositionsSeenUTC             *time.Time          `json:"RawPositionsSeenUtc,omitempty"`
	ObservedLocalPositionComparison *RawLocalComparison `json:"ObservedLocalPositionComparison,omitempty"`
}

// RawSlotPosition is a candidate read using supplied-build default offsets.
// It does not establish a verified local slot or a usable follow position.
type RawSlotPosition struct {
	WorldX                 float64 `json:"WorldX"`
	WorldY                 float64 `json:"WorldY"`
	WorldZ                 float64 `json:"WorldZ"`
	GridX                  float64 `json:"GridX"`
	GridY                  float64 `json:"GridY"`
	RenderComponentAddress string  `json:"RenderComponentAddress"`
}

// RawLocalComparison reports distances to the independently observed local
// Render.Pos in both candidate coordinate scales, without choosing one.
type RawLocalComparison struct {
	ObservedX       float64 `json:"ObservedX"`
	ObservedY       float64 `json:"ObservedY"`
	ObservedZ       float64 `json:"ObservedZ"`
	WorldXYDistance float64 `json:"WorldXYDistance"`
	GridXYDistance  float64 `json:"GridXYDistance"`
}

var ErrInvalidSample = errors.New("invalid telemetry sample")

func Decode(data []byte) (Sample, error) {
	var item Sample
	if err := json.Unmarshal(data, &item); err != nil {
		return Sample{}, err
	}
	if item.ProcessID <= 0 || item.SeenUTC.IsZero() ||
		(item.CoordinateSpace != "" && item.CoordinateSpace != "world") ||
		!finite(item.X) || !finite(item.Y) || !finite(item.Z) ||
		!validPool(item.Life, item.LifeMax) || !validPool(item.Mana, item.ManaMax) ||
		!validPool(item.EnergyShield, item.EnergyShieldMax) ||
		!validAddress(item.IngameDataAddress) || !validAddress(item.ObservedLocalAddress) ||
		!validLocalSlotCandidates(item.LocalSlotCandidates, item.LocalSlotCandidateStatus,
			item.ObservedLocalAddress, item.IngameDataAddress, item.SeenUTC) ||
		!validRadar(item.RadarEntities, item.RadarStatus, item.RadarSeenUTC) ||
		!validGroundHover(item.GroundHover, item.GroundHoverStatus, item.RadarSeenUTC) ||
		!validRawItemCards(item.ItemCards, item.ItemCardStatus, item.ItemCardSeenUTC,
			item.LocalSlotCandidates) ||
		!validRawTerrain(item.TerrainSummary, item.TerrainStatus, item.TerrainSeenUTC) ||
		!validPackedTerrainProbe(item.PackedTerrainProbe, item.PackedTerrainStatus, item.TerrainSeenUTC) ||
		!validVisiblePlayers(item.VisiblePlayers, item.ObservedLocalAddress) {
		return Sample{}, ErrInvalidSample
	}
	return item, nil
}

func validRawTerrain(summary *RawTerrainSummary, status string, seen *time.Time) bool {
	if status == "" {
		return summary == nil && seen == nil
	}
	if seen == nil || seen.IsZero() {
		return false
	}
	if status == "unavailable" || status == "scan_failed" {
		return summary == nil
	}
	if status != "raw_values_observed" && status != "empty" {
		return false
	}
	if summary == nil || summary.Rows <= 0 || summary.MinColumns < 0 ||
		summary.MaxColumns < summary.MinColumns || summary.SampledCells < 0 ||
		summary.SampledCells > 65_536 || len(summary.Values) > 32 {
		return false
	}
	if status == "empty" {
		return summary.SampledCells == 0 && len(summary.Values) == 0
	}
	if summary.SampledCells == 0 || len(summary.Values) == 0 {
		return false
	}
	counted := 0
	previous := 0
	for i, value := range summary.Values {
		if value.Count <= 0 || (i > 0 && value.Value <= previous) {
			return false
		}
		previous = value.Value
		counted += value.Count
	}
	return counted <= summary.SampledCells &&
		(summary.ValuesTruncated || counted == summary.SampledCells)
}

func validPackedTerrainProbe(probe *PackedTerrainProbe, status string, seen *time.Time) bool {
	if status == "" {
		return probe == nil
	}
	if seen == nil || seen.IsZero() {
		return false
	}
	if status != "candidate_patch_observed" {
		return probe == nil && (status == "missing_anchor_or_grid" ||
			status == "header_unreadable" || status == "ambiguous_candidates" ||
			status == "ambiguous_area_candidates" ||
			status == "no_candidate" || status == "patch_unreadable" ||
			status == "scan_failed")
	}
	if probe == nil || probe.AreaAddress == "" || !validAddress(probe.AreaAddress) ||
		(probe.Anchor != "state_pointer_static_656" && probe.Anchor != "ingame_data_address") ||
		probe.BytesPerRow < 1 || probe.Width != probe.BytesPerRow*2 ||
		probe.Width > 100000 || probe.Height < 1 || probe.Height > 100000 ||
		probe.PatchX < 0 || probe.PatchY < 0 || len(probe.PatchRows) < 1 ||
		len(probe.PatchRows) > 9 || probe.PatchY+len(probe.PatchRows) > probe.Height ||
		probe.SampledNibbles < 2 || probe.SampledNibbles > 8192 ||
		probe.InRangeNibbles < 0 || probe.InRangeNibbles > probe.SampledNibbles ||
		probe.InRangeNibbles*5 < probe.SampledNibbles*4 {
		return false
	}
	width := len(probe.PatchRows[0])
	if width < 1 || width > 9 || probe.PatchX+width > probe.Width {
		return false
	}
	for _, row := range probe.PatchRows {
		if len(row) != width || strings.Trim(row, "01") != "" {
			return false
		}
	}
	return true
}

func validRawItemCards(cards []RawItemCard, status string, seen *time.Time,
	ordered *LocalSlotCandidates) bool {
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
			len(card.LootMatch.Name) > 96 ||
			card.LootMatch.BaseIDCandidate != itemPathBaseID(card.LootMatch.ItemPath)) {
			return false
		}
		if card.SlotCandidate != nil {
			slot := card.SlotCandidate.LocalSlot
			if card.OwnerMatch == nil || ordered == nil ||
				(ordered.Status != "single_slot_candidate" && ordered.Status != "two_slot_candidate") ||
				slot < 1 || slot > len(ordered.OrderedAddresses) ||
				!strings.EqualFold(ordered.OrderedAddresses[slot-1], card.Owner) ||
				card.SlotCandidate.Evidence != "unverified_vector_order_candidate" {
				return false
			}
		}
		objects[card.Object] = true
	}
	return true
}

func itemPathBaseID(path string) string {
	trimmed := strings.TrimRight(strings.ReplaceAll(path, `\`, "/"), "/")
	if !strings.HasPrefix(trimmed, "Metadata/Items/") {
		return ""
	}
	return trimmed[strings.LastIndexByte(trimmed, '/')+1:]
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

func validLocalSlotCandidates(candidate *LocalSlotCandidates, scanStatus, observed, dataAddress string,
	seenUTC time.Time) bool {
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
	if !validAddress(candidate.VectorHeaderAddress) ||
		(want == 0 && (candidate.VectorHeaderAddress != "" || len(candidate.RawPositions) != 0 ||
			candidate.RawPositionsSeenUTC != nil)) ||
		(len(candidate.RawPositions) != 0 && (want == 0 || len(candidate.RawPositions) != want ||
			candidate.VectorHeaderAddress == "" || candidate.RawPositionsSeenUTC == nil)) ||
		(len(candidate.RawPositions) == 0 && candidate.RawPositionsSeenUTC != nil) ||
		(candidate.RawPositionsSeenUTC != nil &&
			(candidate.RawPositionsSeenUTC.IsZero() || candidate.RawPositionsSeenUTC.After(seenUTC) ||
				seenUTC.Sub(*candidate.RawPositionsSeenUTC) > time.Second)) {
		return false
	}
	for _, position := range candidate.RawPositions {
		if position != nil && (!finite(position.WorldX) || !finite(position.WorldY) ||
			!finite(position.WorldZ) || !finite(position.GridX) || !finite(position.GridY) ||
			!validAddress(position.RenderComponentAddress) || position.RenderComponentAddress == "") {
			return false
		}
	}
	if comparison := candidate.ObservedLocalPositionComparison; comparison != nil {
		if len(candidate.RawPositions) != want || candidate.RawPositionsSeenUTC == nil ||
			!finite(comparison.ObservedX) || !finite(comparison.ObservedY) ||
			!finite(comparison.ObservedZ) || !finite(comparison.WorldXYDistance) ||
			!finite(comparison.GridXYDistance) || comparison.WorldXYDistance < 0 ||
			comparison.GridXYDistance < 0 {
			return false
		}
		localIndex := -1
		for index, address := range candidate.OrderedAddresses {
			if address == observed {
				localIndex = index
				break
			}
		}
		if localIndex < 0 || candidate.RawPositions[localIndex] == nil {
			return false
		}
		localRaw := candidate.RawPositions[localIndex]
		worldDistance := math.Hypot(localRaw.WorldX-comparison.ObservedX,
			localRaw.WorldY-comparison.ObservedY)
		gridDistance := math.Hypot(localRaw.GridX-comparison.ObservedX,
			localRaw.GridY-comparison.ObservedY)
		if !consistentDistance(comparison.WorldXYDistance, worldDistance) ||
			!consistentDistance(comparison.GridXYDistance, gridDistance) {
			return false
		}
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

func consistentDistance(reported, computed float64) bool {
	return math.Abs(reported-computed) <= 0.001*math.Max(1, computed)
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
