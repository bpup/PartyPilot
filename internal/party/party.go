package party

import (
	"errors"
	"fmt"
	"math"
	"sort"
	"time"

	"partypilot/internal/focus"
	"partypilot/internal/follow"
	"partypilot/internal/navigation"
)

// Coordinator combines routing and movement decisions. Input and game-data
// adapters are separate, so a command here is not evidence of game movement.
type Coordinator struct {
	router           *focus.Router
	leader           int
	followers        map[int]*follow.Engine
	baseConfig       follow.Config
	lastFocusedLeads bool
	lastManualFocus  int
	lastActiveLeader int
	followTargets    map[int]int
	lastTargets      map[int]int
	planner          *navigation.PathPlanner
}

func New(players []focus.Player, leader int, config follow.Config) (*Coordinator, error) {
	router, err := focus.New(players, leader)
	if err != nil {
		return nil, err
	}
	if !config.Valid() {
		return nil, ErrInvalidFollowConfig
	}
	followers := make(map[int]*follow.Engine)
	var planner *navigation.PathPlanner
	if config.AsyncTargetPath {
		planner = navigation.NewPathPlanner()
	}
	for _, player := range players {
		engine, _ := follow.NewWithPathPlanner(config, planner)
		followers[player.Slot] = engine
	}
	return &Coordinator{
		router: router, leader: leader, lastManualFocus: leader, followers: followers, baseConfig: config,
		followTargets: make(map[int]int), lastTargets: make(map[int]int), planner: planner,
	}, nil
}

// SetStopDistance applies the supplied build's per-player stop bubble and
// three-unit release gap to one follower. The existing catch-up distance is
// still a separate explicit setting.
func (c *Coordinator) SetStopDistance(slot int, distance float64) error {
	if _, exists := c.followers[slot]; !exists {
		return errors.New("follower is not in roster")
	}
	if math.IsNaN(distance) || math.IsInf(distance, 0) || distance < 0 || distance > 200 {
		return fmt.Errorf("invalid stop distance for player %d", slot)
	}
	config := c.baseConfig
	config.StopDistance = distance
	config.ResumeDistance = distance + 3
	engine, ok := follow.NewWithPathPlanner(config, c.planner)
	if !ok {
		return fmt.Errorf("invalid follow configuration for player %d", slot)
	}
	c.followers[slot].Close()
	c.followers[slot] = engine
	delete(c.lastTargets, slot)
	return nil
}

// SetFollowTarget assigns a specific player as one follower's target.
// Self or inactive choices restore Current MAIN, matching the supplied
// navigation setting's FollowTargetChoice normalization.
func (c *Coordinator) SetFollowTarget(slot, target int) error {
	if _, exists := c.followers[slot]; !exists {
		return errors.New("follower is not in roster")
	}
	if _, active := c.followers[target]; target == slot || !active {
		delete(c.followTargets, slot)
	} else {
		c.followTargets[slot] = target
	}
	return nil
}

// SetLastFocusedLeads makes the player holding manual focus the follow leader.
// The replay CLI enables this only when explicitly requested.
func (c *Coordinator) SetLastFocusedLeads(enabled bool) {
	c.lastFocusedLeads = enabled
}

func (c *Coordinator) ResetFollow() {
	for _, engine := range c.followers {
		engine.Reset()
	}
}

func (c *Coordinator) Close() {
	for _, engine := range c.followers {
		engine.Close()
	}
	if c.planner != nil {
		c.planner.Close()
	}
}

func (c *Coordinator) Focus(slot int) error {
	if err := c.router.Focus(slot); err != nil {
		return err
	}
	c.lastManualFocus = slot
	if engine, exists := c.followers[slot]; exists {
		engine.Reset()
	}
	return nil
}

func (c *Coordinator) ReturnToLeader() {
	if c.lastFocusedLeads {
		_ = c.router.Focus(c.lastManualFocus)
	} else {
		c.router.ReturnToLeader()
	}
}

func (c *Coordinator) Focused() focus.Player {
	return c.router.Focused()
}

// ResolvedTargets returns the same target graph that Step uses for this focus.
func (c *Coordinator) ResolvedTargets() map[int]int {
	activeLeader := c.leader
	if c.lastFocusedLeads {
		activeLeader = c.lastManualFocus
	}
	return c.resolveTargets(activeLeader)
}

func (c *Coordinator) Step(now time.Time, positions map[int]follow.Position) map[int]follow.Command {
	return c.StepWithTerrain(now, positions, nil)
}

func (c *Coordinator) StepWithTerrain(now time.Time, positions map[int]follow.Position, terrain *navigation.Grid) map[int]follow.Command {
	return c.stepWithTerrainForSlot(now, positions, func(int) *navigation.Grid { return terrain }, nil)
}

// StepWithTerrains supplies each follower its own caller-provided area map.
func (c *Coordinator) StepWithTerrains(now time.Time, positions map[int]follow.Position, terrains map[int]*navigation.Grid) map[int]follow.Command {
	return c.StepWithTerrainsAndHolds(now, positions, terrains, nil)
}

// StepWithTerrainsAndHolds suppresses a follower's route evaluation while
// retaining its observed position for another player's configured target.
func (c *Coordinator) StepWithTerrainsAndHolds(now time.Time, positions map[int]follow.Position,
	terrains map[int]*navigation.Grid, holds map[int]string) map[int]follow.Command {
	return c.stepWithTerrainForSlot(now, positions, func(slot int) *navigation.Grid { return terrains[slot] }, holds)
}

func (c *Coordinator) stepWithTerrainForSlot(now time.Time, positions map[int]follow.Position,
	terrainFor func(int) *navigation.Grid, holds map[int]string) map[int]follow.Command {
	activeLeader := c.leader
	if c.lastFocusedLeads {
		activeLeader = c.lastManualFocus
	}
	if activeLeader != c.lastActiveLeader {
		for _, engine := range c.followers {
			engine.Reset()
		}
		c.lastActiveLeader = activeLeader
	}
	resolvedTargets := c.ResolvedTargets()
	commands := make(map[int]follow.Command, len(c.followers)-1)
	for slot, engine := range c.followers {
		if slot == activeLeader {
			continue
		}
		if slot == c.router.Focused().Slot {
			engine.Reset()
			commands[slot] = follow.Command{State: follow.Holding, Reason: "manual control"}
			continue
		}
		if reason := holds[slot]; reason != "" {
			engine.Reset()
			commands[slot] = follow.Command{State: follow.Waiting, Reason: reason}
			continue
		}
		target := resolvedTargets[slot]
		if c.lastTargets[slot] != target {
			engine.Reset()
			c.lastTargets[slot] = target
		}
		commands[slot] = engine.StepWithTerrain(now, positions[target], positions[slot], terrainFor(slot))
	}
	return commands
}

// resolveTargets follows the supplied six-player navigation graph: missing,
// self and cyclic target paths return to the current MAIN before movement.
func (c *Coordinator) resolveTargets(activeLeader int) map[int]int {
	players := make([]int, 0, len(c.followers))
	for slot := range c.followers {
		players = append(players, slot)
	}
	sort.Ints(players)
	targets := make(map[int]int, len(players))
	for _, slot := range players {
		requested := c.followTargets[slot]
		if _, active := c.followers[requested]; slot == activeLeader ||
			requested == slot || !active {
			targets[slot] = activeLeader
		} else {
			targets[slot] = requested
		}
	}
	for _, slot := range players {
		if slot == activeLeader {
			continue
		}
		seen := map[int]bool{slot: true}
		for next := targets[slot]; next != activeLeader; next = targets[next] {
			if _, active := c.followers[next]; !active || seen[next] {
				for visited := range seen {
					if visited != activeLeader {
						targets[visited] = activeLeader
					}
				}
				break
			}
			seen[next] = true
		}
	}
	return targets
}
