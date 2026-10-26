package combat

import (
	"fmt"
	"math"
	"sort"
	"time"
)

// Encounter is read-only replay state for the supplied default profile.
type Encounter struct {
	ID    string   `json:"id"`
	State string   `json:"state"`
	Tags  []string `json:"tags"`

	ids       map[string]bool
	engagedAt time.Time
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
		t.current = &Encounter{ID: fmt.Sprintf("enc-%d", t.sequence), State: "approaching",
			ids: monsterIDs(entry)}
		t.current.Tags = classifyEncounter(world, entry, time.Time{})
		return t.current, nil
	}
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
	t.current.ids = monsterIDs(selected)
	t.current.Tags = classifyEncounter(world, selected, t.current.engagedAt)
	return t.current, nil
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
	tags := make([]string, 0, 11)
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
