package autoloot

import (
	"sort"
	"strings"
	"time"
)

// CatalogDefinition and Assignment are supplied explicitly by an offline
// caller. No built-in item list or player assignment is assumed.
type CatalogDefinition struct {
	Key                   string   `json:"key"`
	RuntimeBaseIDs        []string `json:"runtime_base_ids"`
	ItemClass             string   `json:"item_class"`
	RequiredRarity        string   `json:"required_rarity"`
	MatchesAllOfItemClass bool     `json:"matches_all_of_item_class"`
	Assignment            string   `json:"assignment"`
}

type LootIdentity struct {
	InnerItem  string    `json:"inner_item"`
	BaseID     string    `json:"base_id"`
	Rarity     string    `json:"rarity"`
	ItemClass  string    `json:"item_class"`
	ReceivedAt time.Time `json:"received_at"`
}

type CatalogQuery struct {
	Player      int                 `json:"player"`
	Card        Card                `json:"card"`
	Identities  []LootIdentity      `json:"identities"`
	Definitions []CatalogDefinition `json:"definitions"`
}

// ResolveAssignedCatalogKey reproduces the 0.6.17 catalog match and explicit
// assignment branch for offline inspection. It excludes the target's economy
// override, so a result cannot be used as complete pickup authorization.
func ResolveAssignedCatalogKey(query CatalogQuery) string {
	if query.Player < 1 || query.Player > 6 || strings.TrimSpace(query.Card.BaseID) == "" {
		return ""
	}
	var rarity, itemClass string
	for _, identity := range query.Identities {
		if strings.EqualFold(identity.InnerItem, query.Card.SelectedItem) &&
			strings.EqualFold(identity.BaseID, query.Card.BaseID) &&
			absDuration(query.Card.ReceivedAt.Sub(identity.ReceivedAt)) <= 2*time.Second {
			rarity, itemClass = identity.Rarity, identity.ItemClass
			break
		}
	}
	type match struct {
		key            string
		raritySpecific bool
		classWide      bool
	}
	var matches []match
	for _, definition := range query.Definitions {
		if definition.Key == "" || !includesPlayer(definition.Assignment, query.Player) ||
			!matchesDefinition(definition, query.Card.BaseID, rarity, itemClass) {
			continue
		}
		matches = append(matches, match{definition.Key, strings.TrimSpace(definition.RequiredRarity) != "", definition.MatchesAllOfItemClass})
	}
	sort.Slice(matches, func(i, j int) bool {
		a, b := matches[i], matches[j]
		if a.raritySpecific != b.raritySpecific {
			return a.raritySpecific
		}
		if a.classWide != b.classWide {
			return !a.classWide
		}
		return strings.ToLower(a.key) < strings.ToLower(b.key)
	})
	if len(matches) == 0 {
		return ""
	}
	return matches[0].key
}

func matchesDefinition(d CatalogDefinition, baseID, rarity, itemClass string) bool {
	matched := false
	switch {
	case d.Key == "$visible:all":
		matched = strings.TrimSpace(baseID) != "" && !strings.EqualFold(itemClass, "quest-junk")
	case d.MatchesAllOfItemClass:
		matched = strings.TrimSpace(itemClass) != "" && strings.EqualFold(d.ItemClass, strings.TrimSpace(itemClass))
	default:
		for _, id := range d.RuntimeBaseIDs {
			if strings.EqualFold(id, baseID) {
				matched = true
				break
			}
		}
	}
	return matched && (strings.TrimSpace(d.RequiredRarity) == "" || strings.EqualFold(d.RequiredRarity, rarity))
}

func includesPlayer(assignment string, player int) bool {
	switch strings.ToLower(strings.TrimSpace(assignment)) {
	case "p1":
		return player == 1
	case "p2":
		return player == 2
	case "both":
		return player <= 2
	case "p3":
		return player == 3
	case "p4":
		return player == 4
	case "client 2 pair", "standalone pair", "tv pair":
		return player == 3 || player == 4
	case "p5":
		return player == 5
	case "p6":
		return player == 6
	case "client 3 pair":
		return player == 5 || player == 6
	default:
		return false
	}
}

func absDuration(value time.Duration) time.Duration {
	if value < 0 {
		return -value
	}
	return value
}
