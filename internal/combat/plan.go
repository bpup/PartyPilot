package combat

import (
	"errors"
	"fmt"
	"math"
	"reflect"
	"strings"
	"time"
)

// World is an explicit replay input, not a projection of the current sensor.
// Pointer booleans force the caller to provide each safety signal.
type World struct {
	Timestamp       time.Time      `json:"timestamp"`
	AreaInstance    string         `json:"areaInstance"`
	DataAgeMS       *int           `json:"dataAgeMs"`
	SafeZone        *bool          `json:"safeZone"`
	EmergencyActive *bool          `json:"emergencyActive"`
	UI              UI             `json:"ui"`
	Players         map[int]Player `json:"players"`
	Monsters        []Monster      `json:"monsters"`
}

type UI struct {
	Loading       *bool `json:"loading"`
	AnyMenuOpen   *bool `json:"anyMenuOpen"`
	InventoryOpen *bool `json:"inventoryOpen"`
	StashOpen     *bool `json:"stashOpen"`
	VendorOpen    *bool `json:"vendorOpen"`
	ChatOpen      *bool `json:"chatOpen"`
}

type Player struct {
	Alive             *bool    `json:"alive"`
	Loading           *bool    `json:"loading"`
	ActiveCombo       *bool    `json:"activeCombo"`
	DistanceFromMainM *float64 `json:"distanceFromMainM"`
	HealthPercent     *float64 `json:"healthPercent,omitempty"`
}

type Monster struct {
	ID                string          `json:"id"`
	Rarity            string          `json:"rarity"`
	Alive             *bool           `json:"alive"`
	DistanceFromMainM *float64        `json:"distanceFromMainM"`
	DistanceByPlayer  map[int]float64 `json:"distanceByPlayerM"`
	HealthPercent     *float64        `json:"healthPercent,omitempty"`
}

type Plan struct {
	Player   int    `json:"player"`
	Status   string `json:"status"`
	Behavior string `json:"behavior,omitempty"`
	ComboID  string `json:"comboId,omitempty"`
	TargetID string `json:"targetId,omitempty"`
	Reason   string `json:"reason,omitempty"`
}

func (w World) Validate() error {
	if w.Timestamp.IsZero() || strings.TrimSpace(w.AreaInstance) == "" ||
		w.DataAgeMS == nil || *w.DataAgeMS < 0 || w.SafeZone == nil ||
		w.EmergencyActive == nil || w.UI.Loading == nil || w.UI.AnyMenuOpen == nil ||
		w.UI.InventoryOpen == nil || w.UI.StashOpen == nil || w.UI.VendorOpen == nil ||
		w.UI.ChatOpen == nil || w.Players == nil || w.Monsters == nil {
		return errors.New("world safety or area evidence is incomplete")
	}
	for id, player := range w.Players {
		if id < 1 || id > 6 || player.Alive == nil || player.Loading == nil || player.ActiveCombo == nil ||
			player.DistanceFromMainM == nil || !finite(*player.DistanceFromMainM) || *player.DistanceFromMainM < 0 ||
			player.HealthPercent != nil && (!finite(*player.HealthPercent) ||
				*player.HealthPercent < 0 || *player.HealthPercent > 100) {
			return fmt.Errorf("player %d evidence is invalid", id)
		}
	}
	seen := make(map[string]bool, len(w.Monsters))
	for _, monster := range w.Monsters {
		if monster.ID == "" || seen[monster.ID] || monster.Alive == nil ||
			monster.Rarity != "normal" && monster.Rarity != "magic" &&
				monster.Rarity != "rare" && monster.Rarity != "unique" ||
			monster.DistanceFromMainM == nil || !finite(*monster.DistanceFromMainM) ||
			*monster.DistanceFromMainM < 0 || monster.DistanceByPlayer == nil || monster.HealthPercent == nil ||
			(!finite(*monster.HealthPercent) ||
				*monster.HealthPercent < 0 || *monster.HealthPercent > 100) {
			return errors.New("monster evidence is invalid")
		}
		seen[monster.ID] = true
		for player, distance := range monster.DistanceByPlayer {
			if player < 1 || player > 6 || !finite(distance) || distance < 0 {
				return errors.New("monster distance is invalid")
			}
		}
		for player := range w.Players {
			if _, ok := monster.DistanceByPlayer[player]; !ok {
				return errors.New("monster distance for a reported player is missing")
			}
		}
	}
	return nil
}

func Evaluate(profile Profile, world World) ([]Plan, error) {
	var tracker EncounterTracker
	encounter, err := tracker.Update(world)
	if err != nil {
		return nil, err
	}
	return EvaluateEncounter(profile, world, encounter)
}

// EvaluateEncounter uses state retained across ordered replay frames.
func EvaluateEncounter(profile Profile, world World, encounter *Encounter) ([]Plan, error) {
	if err := world.Validate(); err != nil {
		return nil, err
	}
	if err := validateProfile(profile); err != nil {
		return nil, err
	}
	plans := make([]Plan, 0, len(profile.Roles))
	global := globalBlock(profile, world)
	for _, role := range profile.Roles {
		plan := Plan{Player: role.Player}
		if !profile.Enabled {
			plan.Status = "profile_disabled"
		} else if encounter == nil {
			plan.Status = "waiting_no_encounter"
			if *world.SafeZone {
				plan.Reason = "In town"
			} else {
				plan.Reason = "Waiting: no enemies in range"
			}
		} else if encounter.State == "winding-down" || encounter.State == "complete" {
			plan.Status = "waiting_fight_ending"
		} else if global != "" {
			plan.Status = global
		} else if player, ok := world.Players[role.Player]; !ok {
			plan.Status = "BLOCK_PLAYER_DATA_UNAVAILABLE"
		} else if profile.Safety.PauseLoading && *player.Loading {
			plan.Status = "BLOCK_LOADING"
		} else if profile.Safety.PauseDead && !*player.Alive {
			plan.Status = "BLOCK_CHARACTER_DEAD"
		} else {
			behavior := "pack"
			if *world.EmergencyActive || player.HealthPercent != nil && *player.HealthPercent <= 30 {
				behavior = "emergency"
			} else if encounter.hasTag("boss") {
				behavior = "boss"
				if role.Kind == "companion-dps" && encounter.hasTag("multi-rare") {
					behavior = "rare"
				}
			} else if encounter.hasTag("rare-present") || encounter.hasTag("dangerous") {
				behavior = "rare"
			}
			plan.Behavior, plan.ComboID = chooseBehavior(role, behavior, world.Monsters, *player.ActiveCombo)
			if plan.ComboID == "" {
				plan.Status = "waiting_no_eligible_encounter"
			} else {
				plan.Status = "planned_only"
				plan.Reason = "synthetic world; no skill binding or controller output verified"
			}
		}
		plans = append(plans, plan)
	}
	return plans, nil
}

func globalBlock(profile Profile, world World) string {
	if *world.SafeZone {
		return "BLOCK_SAFE_ZONE"
	}
	if *world.DataAgeMS > profile.Safety.StaleDataTimeoutMS {
		return "BLOCK_WORLD_DATA_STALE"
	}
	if profile.Safety.PauseLoading && *world.UI.Loading {
		return "BLOCK_LOADING"
	}
	if profile.Safety.PauseAnyMenu && *world.UI.AnyMenuOpen ||
		profile.Safety.PauseInventory && (*world.UI.InventoryOpen || *world.UI.StashOpen || *world.UI.VendorOpen) ||
		profile.Safety.PauseChat && *world.UI.ChatOpen {
		return "BLOCK_UI_OPEN"
	}
	return ""
}

func chooseBehavior(role Role, preferred string, monsters []Monster, activeCombo bool) (string, string) {
	chain := []string{preferred}
	radius := 60.0
	minimumPack := 3
	if activeCombo {
		radius = math.Max(radius*1.35, radius+8)
		minimumPack = 1
	}
	if preferred == "boss" {
		chain = []string{"boss", "rare", "pack"}
	}
	if preferred == "rare" {
		chain = []string{"rare", "pack"}
	}
	for _, behavior := range chain {
		if behavior == "emergency" {
			for _, monster := range monsters {
				if distance, ok := monster.DistanceByPlayer[role.Player]; ok && *monster.Alive && distance <= radius {
					return behavior, role.Behaviors[behavior]
				}
			}
			return "", ""
		}
		pack, rare, unique := 0, 0, 0
		for _, monster := range monsters {
			distance, ok := monster.DistanceByPlayer[role.Player]
			if !*monster.Alive || !ok || distance > radius {
				continue
			}
			switch monster.Rarity {
			case "normal", "magic":
				pack++
			case "rare":
				rare++
			case "unique":
				unique++
			}
		}
		if behavior == "boss" && unique > 0 || behavior == "rare" && rare > 0 ||
			behavior == "pack" && pack >= minimumPack {
			return behavior, role.Behaviors[behavior]
		}
	}
	return "", ""
}

func validateProfile(profile Profile) error {
	expected := DefaultProfile()
	expected.Enabled = profile.Enabled
	if !reflect.DeepEqual(profile, expected) {
		return errors.New("unsupported combat profile; only supplied 0.6.17 default is implemented")
	}
	return nil
}

func finite(value float64) bool { return !math.IsNaN(value) && !math.IsInf(value, 0) }
