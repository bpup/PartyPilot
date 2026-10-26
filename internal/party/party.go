package party

import (
	"errors"
	"time"

	"partypilot/internal/focus"
	"partypilot/internal/follow"
)

// Coordinator combines routing and movement decisions. Input and game-data
// adapters are separate, so a command here is not evidence of game movement.
type Coordinator struct {
	router           *focus.Router
	leader           int
	followers        map[int]*follow.Engine
	lastFocusedLeads bool
	lastActiveLeader int
	followTargets    map[int]int
	lastTargets      map[int]int
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
	for _, player := range players {
		engine, _ := follow.New(config)
		followers[player.Slot] = engine
	}
	return &Coordinator{
		router: router, leader: leader, followers: followers,
		followTargets: make(map[int]int), lastTargets: make(map[int]int),
	}, nil
}

// SetFollowTarget assigns a specific player as one follower's target.
// Target zero restores Current MAIN routing.
func (c *Coordinator) SetFollowTarget(slot, target int) error {
	if _, exists := c.followers[slot]; !exists {
		return errors.New("follower is not in roster")
	}
	if target == slot {
		return errors.New("player cannot follow itself")
	}
	if target != 0 {
		if _, exists := c.followers[target]; !exists {
			return errors.New("follow target is not in roster")
		}
	}
	if target == 0 {
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

func (c *Coordinator) Focus(slot int) error {
	if err := c.router.Focus(slot); err != nil {
		return err
	}
	if engine, exists := c.followers[slot]; exists {
		engine.Reset()
	}
	return nil
}

func (c *Coordinator) ReturnToLeader() {
	c.router.ReturnToLeader()
}

func (c *Coordinator) Focused() focus.Player {
	return c.router.Focused()
}

func (c *Coordinator) Step(now time.Time, positions map[int]follow.Position) map[int]follow.Command {
	activeLeader := c.leader
	if c.lastFocusedLeads {
		activeLeader = c.router.Focused().Slot
	}
	if activeLeader != c.lastActiveLeader {
		for _, engine := range c.followers {
			engine.Reset()
		}
		c.lastActiveLeader = activeLeader
	}
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
		target := activeLeader
		if specific := c.followTargets[slot]; specific != 0 {
			target = specific
		}
		if c.lastTargets[slot] != target {
			engine.Reset()
			c.lastTargets[slot] = target
		}
		commands[slot] = engine.Step(now, positions[target], positions[slot])
	}
	return commands
}
