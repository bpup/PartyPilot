package party

import (
	"time"

	"partypilot/internal/focus"
	"partypilot/internal/follow"
)

// Coordinator combines routing and movement decisions. Input and game-data
// adapters are separate, so a command here is not evidence of game movement.
type Coordinator struct {
	router    *focus.Router
	leader    int
	followers map[int]*follow.Engine
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
		if player.Slot == leader {
			continue
		}
		engine, _ := follow.New(config)
		followers[player.Slot] = engine
	}
	return &Coordinator{router: router, leader: leader, followers: followers}, nil
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
	commands := make(map[int]follow.Command, len(c.followers))
	leader := positions[c.leader]
	for slot, engine := range c.followers {
		if !c.router.AutomationAllowed(slot) {
			engine.Reset()
			commands[slot] = follow.Command{State: follow.Holding, Reason: "manual control"}
			continue
		}
		commands[slot] = engine.Step(now, leader, positions[slot])
	}
	return commands
}
