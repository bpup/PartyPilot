package combat

import (
	"math"
	"sort"
	"time"
)

type targetClaim struct {
	id    string
	until time.Time
}

// TargetCoordinator retains short-lived target claims across replay frames.
// It only annotates plans; it cannot issue game input.
type TargetCoordinator struct {
	claims map[int]targetClaim
}

func (c *TargetCoordinator) Assign(profile Profile, world World, plans []Plan) {
	if c.claims == nil {
		c.claims = make(map[int]targetClaim)
	}
	alive := make([]Monster, 0, len(world.Monsters))
	rares := make([]Monster, 0)
	unique := make([]Monster, 0)
	for _, monster := range world.Monsters {
		if !*monster.Alive {
			continue
		}
		alive = append(alive, monster)
		switch monster.Rarity {
		case "rare":
			rares = append(rares, monster)
		case "unique":
			unique = append(unique, monster)
		}
	}
	for player, claim := range c.claims {
		if !claim.until.After(world.Timestamp) || !hasMonster(alive, claim.id) {
			delete(c.claims, player)
		}
	}
	sort.SliceStable(rares, func(i, j int) bool {
		return *rares[i].DistanceFromMainM < *rares[j].DistanceFromMainM
	})
	sort.SliceStable(unique, func(i, j int) bool {
		return *unique[i].DistanceFromMainM < *unique[j].DistanceFromMainM
	})
	roles := make(map[int]Role, len(profile.Roles))
	for _, role := range profile.Roles {
		roles[role.Player] = role
	}
	mainTarget := ""
	for i := range plans {
		if plans[i].Player == 1 && plans[i].Status == "planned_only" {
			mainTarget = c.existing(1, alive)
			if mainTarget == "" {
				mainTarget = selectTarget(roles[1].Targets[plans[i].Behavior], 1, alive, unique, nil)
			}
			plans[i].TargetID = mainTarget
			c.record(1, mainTarget, alive, world.Timestamp)
			break
		}
	}
	claimedRares := make(map[string]bool)
	for _, plan := range plans {
		if plan.Status != "planned_only" || roles[plan.Player].Targets[plan.Behavior] != "assigned-target" {
			continue
		}
		if id := c.existing(plan.Player, rares); id != "" {
			claimedRares[id] = true
		}
	}
	for i := range plans {
		plan := &plans[i]
		if plan.Player == 1 || plan.Status != "planned_only" {
			continue
		}
		strategy := roles[plan.Player].Targets[plan.Behavior]
		if strategy == "assigned-target" && roles[plan.Player].Kind == "companion-dps" {
			plan.TargetID = c.existing(plan.Player, rares)
			if plan.TargetID == "" {
				bestDistance := math.Inf(1)
				for _, monster := range rares {
					if distance := monster.DistanceByPlayer[plan.Player]; !claimedRares[monster.ID] && distance < bestDistance {
						bestDistance, plan.TargetID = distance, monster.ID
					}
				}
			}
			if plan.TargetID != "" {
				claimedRares[plan.TargetID] = true
			} else {
				plan.TargetID = mainTarget
			}
		} else {
			plan.TargetID = selectTarget(strategy, plan.Player, alive, unique, &mainTarget)
		}
		c.record(plan.Player, plan.TargetID, alive, world.Timestamp)
	}
	combos := make(map[string]Combo, len(profile.Combos))
	for _, combo := range profile.Combos {
		combos[combo.ID] = combo
	}
	for i := range plans {
		plan := &plans[i]
		if plan.Status != "planned_only" || plan.TargetID != "" ||
			!requiresSpecificTarget(combos[plan.ComboID]) {
			continue
		}
		bestDistance := math.Inf(1)
		for _, monster := range alive {
			if distance, ok := monster.DistanceByPlayer[plan.Player]; ok && distance < bestDistance {
				bestDistance, plan.TargetID = distance, monster.ID
			}
		}
	}
	for i := range plans {
		if plans[i].Status == "planned_only" {
			plans[i].Aim = AimAt(world, plans[i].Player, plans[i].TargetID)
		}
	}
}

func requiresSpecificTarget(combo Combo) bool {
	for _, step := range combo.Steps {
		switch step.Condition {
		case "target-alive", "finisher-condition":
			return true
		}
	}
	return false
}

func (c *TargetCoordinator) existing(player int, allowed []Monster) string {
	claim, ok := c.claims[player]
	if ok && hasMonster(allowed, claim.id) {
		return claim.id
	}
	return ""
}

func (c *TargetCoordinator) record(player int, id string, monsters []Monster, now time.Time) {
	if id == "" {
		delete(c.claims, player)
		return
	}
	for _, monster := range monsters {
		if monster.ID != id {
			continue
		}
		keep := 400 * time.Millisecond
		switch monster.Rarity {
		case "rare":
			keep = 800 * time.Millisecond
		case "unique":
			keep = 1200 * time.Millisecond
		}
		c.claims[player] = targetClaim{id, now.Add(keep)}
		return
	}
	delete(c.claims, player)
}

func selectTarget(strategy string, player int, alive, unique []Monster, mainTarget *string) string {
	switch strategy {
	case "none", "party-centre":
		return ""
	case "main-target":
		if mainTarget != nil {
			return *mainTarget
		}
		return ""
	case "unique":
		if len(unique) > 0 {
			return unique[0].ID
		}
		return ""
	case "nearest-valid":
		bestDistance, target := math.Inf(1), ""
		for _, monster := range alive {
			if distance := monster.DistanceByPlayer[player]; distance < bestDistance {
				bestDistance, target = distance, monster.ID
			}
		}
		return target
	case "highest-threat", "assigned-target":
		bestScore, target := math.Inf(-1), ""
		for _, monster := range alive {
			score := threatScore(monster, player)
			if score > bestScore {
				bestScore, target = score, monster.ID
			}
		}
		return target
	default:
		return ""
	}
}

func threatScore(monster Monster, player int) float64 {
	base := 10.0
	switch monster.Rarity {
	case "magic":
		base = 35
	case "rare":
		base = 100
	case "unique":
		base = 90
	}
	distance, ok := monster.DistanceByPlayer[player]
	if !ok {
		distance = *monster.DistanceFromMainM
	}
	return base - math.Min(40, distance)
}

func hasMonster(monsters []Monster, id string) bool {
	for _, monster := range monsters {
		if monster.ID == id {
			return true
		}
	}
	return false
}
