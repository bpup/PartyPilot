package autoloot

import (
	"testing"
	"time"
)

func TestResolveAssignedCatalogKey(t *testing.T) {
	now := time.Date(2026, 10, 26, 0, 0, 0, 0, time.UTC)
	query := CatalogQuery{
		Player:     2,
		Card:       Card{SelectedItem: "0xA", BaseID: "SomeBase", ReceivedAt: now},
		Identities: []LootIdentity{{InnerItem: "0xA", BaseID: "SomeBase", Rarity: "Unique", ItemClass: "Jewel", ReceivedAt: now.Add(time.Second)}},
		Definitions: []CatalogDefinition{
			{Key: "generic", RuntimeBaseIDs: []string{"SomeBase"}, Assignment: "Both"},
			{Key: "class", ItemClass: "Jewel", MatchesAllOfItemClass: true, Assignment: "P2"},
			{Key: "rare", RuntimeBaseIDs: []string{"SomeBase"}, RequiredRarity: "Unique", Assignment: "P2"},
		},
	}
	if got := ResolveAssignedCatalogKey(query); got != "rare" {
		t.Fatalf("got %q", got)
	}
	query.Identities[0].ReceivedAt = now.Add(3 * time.Second)
	if got := ResolveAssignedCatalogKey(query); got != "generic" {
		t.Fatalf("stale identity: %q", got)
	}
	query.Player = 3
	if got := ResolveAssignedCatalogKey(query); got != "" {
		t.Fatalf("wrong player: %q", got)
	}
}

func TestVisibleAllExcludesQuestJunk(t *testing.T) {
	query := CatalogQuery{Player: 1, Card: Card{SelectedItem: "a", BaseID: "b"}, Definitions: []CatalogDefinition{{Key: "$visible:all", Assignment: "P1"}}}
	if got := ResolveAssignedCatalogKey(query); got != "$visible:all" {
		t.Fatalf("got %q", got)
	}
	query.Identities = []LootIdentity{{InnerItem: "a", BaseID: "b", ItemClass: "quest-junk"}}
	if got := ResolveAssignedCatalogKey(query); got != "" {
		t.Fatalf("quest junk: %q", got)
	}
}

func TestAssignmentPairs(t *testing.T) {
	for _, tc := range []struct {
		assignment string
		player     int
		want       bool
	}{
		{"Client 2 Pair", 3, true}, {"Client 2 Pair", 5, false},
		{"Client 3 Pair", 6, true}, {"Both", 3, false}, {"unknown", 1, false},
	} {
		if got := includesPlayer(tc.assignment, tc.player); got != tc.want {
			t.Fatalf("%s P%d: %v", tc.assignment, tc.player, got)
		}
	}
}
