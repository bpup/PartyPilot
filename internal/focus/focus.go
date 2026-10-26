package focus

import "errors"

type Player struct {
	Slot   int
	Client int
}

type Router struct {
	players map[int]Player
	leader  int
	focused int
}

func New(players []Player, leader int) (*Router, error) {
	if len(players) == 0 || len(players) > 6 {
		return nil, errors.New("roster must contain 1 to 6 players")
	}
	indexed := make(map[int]Player, len(players))
	perClient := make(map[int]int)
	for _, player := range players {
		if player.Slot < 1 || player.Slot > 6 || player.Client < 1 || player.Client > 3 {
			return nil, errors.New("player slot or client out of range")
		}
		if _, exists := indexed[player.Slot]; exists {
			return nil, errors.New("duplicate player slot")
		}
		indexed[player.Slot] = player
		perClient[player.Client]++
		if perClient[player.Client] > 2 {
			return nil, errors.New("more than two players in one client")
		}
	}
	if _, exists := indexed[leader]; !exists {
		return nil, errors.New("leader is not in roster")
	}
	return &Router{players: indexed, leader: leader, focused: leader}, nil
}

func (r *Router) Focus(slot int) error {
	if _, exists := r.players[slot]; !exists {
		return errors.New("player is not in roster")
	}
	r.focused = slot
	return nil
}

func (r *Router) ReturnToLeader() {
	r.focused = r.leader
}

func (r *Router) Focused() Player {
	return r.players[r.focused]
}

// AutomationAllowed is a routing decision only. It does not enable any game input.
func (r *Router) AutomationAllowed(slot int) bool {
	_, exists := r.players[slot]
	return exists && slot != r.leader && slot != r.focused
}

func (r *Router) PhysicalInputTarget() Player {
	return r.players[r.focused]
}
