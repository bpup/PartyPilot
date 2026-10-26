package combat

import (
	"fmt"
	"math"
	"sort"
	"time"
)

// Encounter is read-only replay state for the supplied default profile.
type Encounter struct {
	ID      string   `json:"id"`
	Version int      `json:"version"`
	State   string   `json:"state"`
	Tags    []string `json:"tags"`

	ids           map[string]bool
	counts        [4]int
	primaryThreat string
	engagedAt     time.Time
}

// EncounterTracker models the default 60 m entry radius, 75 m exit radius,
// 150 ms entry debounce and 800 ms exit debounce over ordered replay frames.
type EncounterTracker struct {
	sequence int
	current  *Encounter
	entryAt  time.Time
	exitAt   time.Time
}

func (t *EncounterTracker) Update(world World) (*Encounter, error) {
	if err := world.Validate(); err != nil {
		return nil, err
	}
	if t.current != nil && t.current.State == "complete" {
		t.current = nil
		t.entryAt = time.Time{}
		t.exitAt = time.Time{}
		return nil, nil
	}
	alive := make([]Monster, 0, len(world.Monsters))
	entry := make([]Monster, 0, len(world.Monsters))
	for _, monster := range world.Monsters {
		if !*monster.Alive {
			continue
		}
		alive = append(alive, monster)
		if nearestDistance(monster) <= 60 {
			entry = append(entry, monster)
		}
	}
	if t.current == nil {
		threat := false
		for _, monster := range entry {
			if monster.Rarity == "rare" || monster.Rarity == "unique" {
				threat = true
				break
			}
		}
		if len(entry) < 3 && !threat {
			t.entryAt = time.Time{}
			return nil, nil
		}
		t.sequence++
		t.entryAt = world.Timestamp
		t.current = &Encounter{ID: fmt.Sprintf("enc-%d", t.sequence), Version: 1,
			State: "approaching", ids: monsterIDs(entry), counts: monsterCounts(entry),
			primaryThreat: primaryThreat(entry)}
		t.current.Tags = classifyEncounter(world, entry, time.Time{})
		if len(t.current.Tags) > 0 {
			t.current.Version++
		}
		return t.current, nil
	}
	previousState := t.current.State
	previousTags := t.current.Tags
	selected := make([]Monster, 0, len(alive))
	for _, monster := range alive {
		if t.current.ids[monster.ID] || nearestDistance(monster) <= 75 {
			selected = append(selected, monster)
		}
	}
	if len(selected) == 0 {
		if t.exitAt.IsZero() {
			t.exitAt = world.Timestamp
		}
		if world.Timestamp.Sub(t.exitAt) >= 800*time.Millisecond {
			t.current.State = "complete"
		} else {
			t.current.State = "winding-down"
		}
	} else {
		t.exitAt = time.Time{}
		switch t.current.State {
		case "approaching":
			if world.Timestamp.Sub(t.entryAt) >= 150*time.Millisecond {
				t.current.State = "engaged"
				t.current.engagedAt = world.Timestamp
			}
		case "engaged", "winding-down":
			t.current.State = "active"
		}
	}
	ids := monsterIDs(selected)
	counts := monsterCounts(selected)
	threat := primaryThreat(selected)
	if t.current.State != previousState || !sameMonsterIDs(t.current.ids, ids) ||
		t.current.counts != counts || t.current.primaryThreat != threat {
		t.current.Version++
	}
	t.current.ids = ids
	t.current.counts = counts
	t.current.primaryThreat = threat
	tags := classifyEncounter(world, selected, t.current.engagedAt)
	if !sameTags(previousTags, tags) {
		t.current.Version++
	}
	t.current.Tags = tags
	return t.current, nil
}

func sameMonsterIDs(left, right map[string]bool) bool {
	if len(left) != len(right) {
		return false
	}
	for id := range left {
		if !right[id] {
			return false
		}
	}
	return true
}

func sameTags(left, right []string) bool {
	if len(left) != len(right) {
		return false
	}
	for i := range left {
		if left[i] != right[i] {
			return false
		}
	}
	return true
}

func monsterCounts(monsters []Monster) [4]int {
	var counts [4]int
	for _, monster := range monsters {
		switch monster.Rarity {
		case "normal":
			counts[0]++
		case "magic":
			counts[1]++
		case "rare":
			counts[2]++
		case "unique":
			counts[3]++
		}
	}
	return counts
}

func primaryThreat(monsters []Monster) string {
	bestID := ""
	bestRank := -1
	bestDistance := math.Inf(1)
	for _, monster := range monsters {
		rank := 100
		switch monster.Rarity {
		case "magic":
			rank = 200
		case "rare":
			rank = 300
		case "unique":
			rank = 400
		}
		distance := nearestDistance(monster)
		if rank > bestRank || rank == bestRank && distance < bestDistance {
			bestID, bestRank, bestDistance = monster.ID, rank, distance
		}
	}
	return bestID
}

func monsterIDs(monsters []Monster) map[string]bool {
	ids := make(map[string]bool, len(monsters))
	for _, monster := range monsters {
		ids[monster.ID] = true
	}
	return ids
}

func nearestDistance(monster Monster) float64 {
	if len(monster.DistanceByPlayer) == 0 {
		return *monster.DistanceFromMainM
	}
	nearest := math.Inf(1)
	for _, distance := range monster.DistanceByPlayer {
		nearest = math.Min(nearest, distance)
	}
	return nearest
}

func classifyEncounter(world World, selected []Monster, engagedAt time.Time) []string {
	counts := map[string]int{"normal": 0, "magic": 0, "rare": 0, "unique": 0}
	for _, monster := range selected {
		counts[monster.Rarity]++
	}
	tags := make([]string, 0, 12)
	add := func(yes bool, name string) {
		if yes {
			tags = append(tags, name)
		}
	}
	add(counts["normal"]+counts["magic"] >= 3, "pack")
	add(len(selected) > 0 && len(selected) < 3, "small-pack")
	add(len(selected) >= 10, "large-pack")
	add(counts["magic"] >= 5 || len(selected) > 0 && float64(counts["magic"])/float64(len(selected)) >= 0.4, "magic-heavy")
	add(counts["rare"] >= 1, "rare-present")
	add(counts["rare"] >= 2, "multi-rare")
	add(counts["unique"] >= 1, "unique-present")
	add(counts["unique"] >= 1, "boss")
	surrounded := 0
	for _, monster := range world.Monsters {
		if *monster.Alive && nearestDistance(monster) <= 7 {
			surrounded++
		}
	}
	add(counts["unique"] > 0 || counts["rare"] >= 2 || surrounded >= 8, "dangerous")
	add(surrounded >= 8, "surrounded")
	add(len(selected) == 1 && counts["rare"]+counts["unique"] == 1, "isolated-threat")
	add(!engagedAt.IsZero() && world.Timestamp.Sub(engagedAt) >= 8*time.Second &&
		counts["rare"]+counts["unique"] > 0, "long-fight")
	formationBroken := false
	for _, player := range world.Players {
		if *player.DistanceFromMainM > 15 {
			formationBroken = true
			break
		}
	}
	add(formationBroken, "formation-broken")
	sort.Strings(tags)
	return tags
}

func (e *Encounter) hasTag(tag string) bool {
	if e == nil {
		return false
	}
	for _, current := range e.Tags {
		if current == tag {
			return true
		}
	}
	return false
}
