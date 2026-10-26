package flask

import (
	"errors"
	"math"
	"time"
)

type Pool struct {
	Current *float64 `json:"current"`
	Maximum *float64 `json:"maximum"`
}

type Vitals struct {
	Life Pool `json:"life"`
	Mana Pool `json:"mana"`
}

type Rule struct {
	Enabled        bool
	Trigger        float64
	Cooldown       time.Duration
	MinMissing     float64
	Emergency      *float64
	SkipRecovering bool
	Sustained      bool
}

type Settings struct {
	Life Rule
	Mana Rule
}

type Activation struct {
	Player   int     `json:"player"`
	Resource string  `json:"resource"`
	Percent  float64 `json:"percent"`
	Reason   string  `json:"reason"`
}

type ruleState struct {
	last      time.Time
	latched   bool
	emergency bool
}

type playerState struct {
	lastStep time.Time
	life     ruleState
	mana     ruleState
}

type Engine struct {
	players map[int]*playerState
}

func New() *Engine { return &Engine{players: make(map[int]*playerState)} }

// ResetPlayer clears cooldown and threshold state after a player session changes.
func (engine *Engine) ResetPlayer(player int) { delete(engine.players, player) }

// ResetProfiles match the life and mana reset profiles observed in 0.6.17.
// Ward, ES, custom actions, support and empty-flask detection are separate rules.
func ResetProfiles(player int) (Settings, error) {
	if player < 1 || player > 6 {
		return Settings{}, errors.New("player must be 1–6")
	}
	settings := Settings{
		Life: Rule{Enabled: true, Trigger: 70, Cooldown: 200 * time.Millisecond, MinMissing: 0, SkipRecovering: true},
		Mana: Rule{Enabled: true, Trigger: 45, Cooldown: 800 * time.Millisecond, MinMissing: 0},
	}
	if player%2 == 0 {
		settings.Life.Cooldown = 400 * time.Millisecond
		settings.Life.MinMissing = 25
		settings.Mana.MinMissing = 20
		if player == 2 {
			settings.Life.Trigger = 45
		}
	}
	emergency := 25.0
	if player%2 == 0 {
		emergency = 20
	}
	settings.Life.Emergency = &emergency
	return settings, nil
}

func (engine *Engine) Step(player int, now time.Time, vitals Vitals, rules Settings, menuOpen, recovering bool) ([]Activation, error) {
	if player < 1 || player > 6 || now.IsZero() || !validRule(rules.Life) || !validRule(rules.Mana) ||
		!validPool(vitals.Life) || !validPool(vitals.Mana) {
		return nil, errors.New("invalid player, time, rule, or vitals")
	}
	state := engine.players[player]
	if state == nil {
		state = &playerState{}
		engine.players[player] = state
	}
	if !state.lastStep.IsZero() && now.Before(state.lastStep) {
		return nil, errors.New("out-of-order flask frame")
	}
	state.lastStep = now
	if menuOpen {
		return nil, nil
	}
	var activations []Activation
	if result := evaluate("life", player, now, vitals.Life, rules.Life, &state.life, rules.Life.SkipRecovering && recovering); result != nil {
		activations = append(activations, *result)
	}
	if result := evaluate("mana", player, now, vitals.Mana, rules.Mana, &state.mana, false); result != nil {
		activations = append(activations, *result)
	}
	return activations, nil
}

func evaluate(resource string, player int, now time.Time, pool Pool, rule Rule, state *ruleState, held bool) *Activation {
	if !rule.Enabled {
		state.latched, state.emergency = false, false
		return nil
	}
	if pool.Current == nil || pool.Maximum == nil || *pool.Maximum <= 0 {
		return nil
	}
	percent := min(100, max(0, *pool.Current / *pool.Maximum * 100))
	missing := *pool.Maximum - *pool.Current
	if percent > rule.Trigger || missing < rule.MinMissing {
		state.latched, state.emergency = false, false
		return nil
	}
	if held {
		return nil
	}
	emergencyEdge := rule.Emergency != nil && percent <= *rule.Emergency && !state.emergency
	if emergencyEdge {
		state.emergency = true
	}
	if !emergencyEdge && state.latched && !rule.Sustained {
		return nil
	}
	if !emergencyEdge && !state.last.IsZero() && now.Sub(state.last) < rule.Cooldown {
		return nil
	}
	reason := "threshold"
	if emergencyEdge {
		reason = "emergency"
	} else if state.latched && rule.Sustained {
		reason = "sustained"
	}
	state.latched = true
	state.last = now
	return &Activation{Player: player, Resource: resource, Percent: percent, Reason: reason}
}

func validRule(rule Rule) bool {
	if math.IsNaN(rule.Trigger) || math.IsInf(rule.Trigger, 0) || rule.Trigger < 0 || rule.Trigger > 100 ||
		rule.Cooldown < 0 || math.IsNaN(rule.MinMissing) || math.IsInf(rule.MinMissing, 0) || rule.MinMissing < 0 {
		return false
	}
	return rule.Emergency == nil || !math.IsNaN(*rule.Emergency) && !math.IsInf(*rule.Emergency, 0) &&
		*rule.Emergency >= 0 && *rule.Emergency <= rule.Trigger
}

func validPool(pool Pool) bool {
	if pool.Current == nil && pool.Maximum == nil {
		return true
	}
	if pool.Current == nil || pool.Maximum == nil {
		return false
	}
	for _, value := range []*float64{pool.Current, pool.Maximum} {
		if math.IsNaN(*value) || math.IsInf(*value, 0) || *value < 0 {
			return false
		}
	}
	return true
}
