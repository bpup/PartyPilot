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
	VisiblePlayers           []VisiblePlayer      `json:"VisiblePlayers,omitempty"`
	VisiblePlayersStatus     string               `json:"VisiblePlayersStatus,omitempty"`
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

// LocalSlotCandidates are read-only hints from an unverified memory anchor.
// They must never be used as a controller or party slot assignment.
type LocalSlotCandidates struct {
	Status           string   `json:"Status"`
	Anchor           string   `json:"Anchor"`
	OffsetProfile    string   `json:"OffsetProfile"`
	OrderedAddresses []string `json:"OrderedAddresses"`
	MatchingHeaders  int      `json:"MatchingHeaders"`
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
		!validLocalSlotCandidates(item.LocalSlotCandidates, item.LocalSlotCandidateStatus, item.ObservedLocalAddress) ||
		!validVisiblePlayers(item.VisiblePlayers, item.ObservedLocalAddress) {
		return Sample{}, ErrInvalidSample
	}
	return item, nil
}

func validLocalSlotCandidates(candidate *LocalSlotCandidates, scanStatus, observed string) bool {
	if scanStatus != "" && scanStatus != "ok" && scanStatus != "scan_failed" {
		return false
	}
	if candidate == nil {
		return scanStatus != "ok"
	}
	if scanStatus != "ok" || candidate.Anchor == "" || candidate.OffsetProfile == "" ||
		candidate.MatchingHeaders < 0 {
		return false
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
