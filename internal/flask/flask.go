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
	Life         Pool `json:"life"`
	Mana         Pool `json:"mana"`
	EnergyShield Pool `json:"energy_shield"`
	Ward         Pool `json:"ward"`
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
	Life                Rule
	Mana                Rule
	SupportBuild        bool
	BuildHasNoMana      bool
	SustainedUse        bool
	EsTrigger           float64
	ChaosInoculation    bool
	CombinedDefence     bool
	WardEnabled         bool
	WardDelaysLife      bool
	FireLifeOnWardBreak bool
}

type Activation struct {
	Player      int     `json:"player"`
	Resource    string  `json:"resource"`
	Percent     float64 `json:"percent"`
	Reason      string  `json:"reason"`
	DirectInput string  `json:"direct_input,omitempty"`
}

type ruleState struct {
	last      time.Time
	latched   bool
	emergency bool
}

type playerState struct {
	lastStep   time.Time
	life       ruleState
	mana       ruleState
	lastWard   *float64
	empty      map[string]*emptyFlaskWatch
	customLast map[string]time.Time
}

type emptyFlaskWatch struct {
	pressedAt      time.Time
	pressedPercent float64
	awaitingResult bool
	misses         int
	empty          bool
	emptyPercent   float64
}

const (
	emptyFlaskVerifyDelay = 900 * time.Millisecond
	emptyFlaskRecovery    = 1.5
	emptyFlaskMissLimit   = 3
	emptyFlaskRearm       = 3.0
)

type Engine struct {
	players map[int]*playerState
	support map[supportKey]bool
}

func New() *Engine {
	return &Engine{players: make(map[int]*playerState), support: make(map[supportKey]bool)}
}

// ResetPlayer clears cooldown and threshold state after a player session changes.
func (engine *Engine) ResetPlayer(player int) {
	delete(engine.players, player)
	for key := range engine.support {
		if key.drinker == player || key.target == player {
			delete(engine.support, key)
		}
	}
}

// ResetProfiles match the resource reset profiles observed in 0.6.17.
// Support is opt-in and requires separate watched-player vitals.
func ResetProfiles(player int) (Settings, error) {
	if player < 1 || player > 6 {
		return Settings{}, errors.New("player must be 1–6")
	}
	settings := Settings{
		Life:      Rule{Enabled: true, Trigger: 70, Cooldown: 200 * time.Millisecond, MinMissing: 0, SkipRecovering: true},
		Mana:      Rule{Enabled: true, Trigger: 45, Cooldown: 800 * time.Millisecond, MinMissing: 0},
		EsTrigger: 35, ChaosInoculation: true, WardEnabled: true,
		WardDelaysLife: true, FireLifeOnWardBreak: true,
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
	return engine.StepWithCustom(player, now, vitals, rules, menuOpen, recovering, nil, true)
}

// StepWithCustom evaluates target-version custom recovery rules after the
// built-in life and mana checks. In-combat state must be supplied by the caller.
func (engine *Engine) StepWithCustom(player int, now time.Time, vitals Vitals, rules Settings,
	menuOpen, recovering bool, custom []CustomRule, inCombat bool) ([]Activation, error) {
	if player < 1 || player > 6 || now.IsZero() || !validRule(rules.Life) || !validRule(rules.Mana) ||
		math.IsNaN(rules.EsTrigger) || math.IsInf(rules.EsTrigger, 0) || rules.EsTrigger < 0 || rules.EsTrigger > 100 ||
		!validPool(vitals.Life) || !validPool(vitals.Mana) || !validPool(vitals.EnergyShield) || !validWard(vitals.Ward) {
		return nil, errors.New("invalid player, time, rule, or vitals")
	}
	if err := ValidateCustomRules(custom); err != nil {
		return nil, err
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
	state.verifyEmptyFlasks(now, vitals)
	if menuOpen {
		return nil, nil
	}
	var activations []Activation
	wardBroke := state.lastWard != nil && *state.lastWard > 0 && vitals.Ward.Current != nil && *vitals.Ward.Current <= 0
	state.lastWard = copyNumber(vitals.Ward.Current)
	lifePool := vitals.Life
	lifeRule := rules.Life
	lifeRule.Sustained = lifeRule.Sustained || rules.SustainedUse
	if rules.ChaosInoculation && vitals.Life.Maximum != nil && *vitals.Life.Maximum <= 1 &&
		vitals.EnergyShield.Maximum != nil && *vitals.EnergyShield.Maximum > 0 {
		lifePool = vitals.EnergyShield
		lifeRule.Trigger = rules.EsTrigger
	} else if rules.CombinedDefence && vitals.Life.Current != nil && vitals.Life.Maximum != nil &&
		vitals.EnergyShield.Current != nil && vitals.EnergyShield.Maximum != nil {
		lifePool = Pool{Current: number(*vitals.Life.Current + *vitals.EnergyShield.Current),
			Maximum: number(*vitals.Life.Maximum + *vitals.EnergyShield.Maximum)}
	}
	if wardBroke && rules.FireLifeOnWardBreak && vitals.Life.Current != nil && vitals.Life.Maximum != nil {
		forced := Rule{Enabled: true, Trigger: 100, Emergency: number(100)}
		if result := evaluate("life", player, now, vitals.Life, forced, &state.life, false, true); result != nil {
			activations = append(activations, *result)
		}
	} else if result := evaluate("life", player, now, lifePool, lifeRule, &state.life,
		(rules.Life.SkipRecovering && recovering) || (rules.WardEnabled && rules.WardDelaysLife &&
			vitals.Ward.Current != nil && *vitals.Ward.Current > 0), false); result != nil {
		activations = append(activations, *result)
	}
	manaRule := rules.Mana
	manaRule.Enabled = manaRule.Enabled && !rules.BuildHasNoMana
	manaRule.Sustained = manaRule.Sustained || rules.SustainedUse
	if result := evaluate("mana", player, now, vitals.Mana, manaRule, &state.mana, false, false); result != nil {
		activations = append(activations, *result)
	}
	activations = append(activations, state.evaluateCustom(player, now, vitals, custom, inCombat, wardBroke)...)
	return state.withoutEmptyFlasks(now, vitals, activations), nil
}

func (state *playerState) verifyEmptyFlasks(now time.Time, vitals Vitals) {
	for resource, watch := range state.empty {
		percent, known := resourcePercent(vitals, resource)
		if !known {
			continue
		}
		if watch.awaitingResult && now.Sub(watch.pressedAt) >= emptyFlaskVerifyDelay {
			watch.awaitingResult = false
			if percent-watch.pressedPercent >= emptyFlaskRecovery {
				watch.misses = 0
				watch.empty = false
			} else {
				watch.misses++
				if watch.misses >= emptyFlaskMissLimit {
					watch.empty = true
					watch.emptyPercent = percent
				}
			}
		}
		if watch.empty && percent-watch.emptyPercent >= emptyFlaskRearm {
			watch.empty = false
			watch.misses = 0
		}
	}
}

func (state *playerState) withoutEmptyFlasks(now time.Time, vitals Vitals, activations []Activation) []Activation {
	if len(activations) == 0 {
		return activations
	}
	if state.empty == nil {
		state.empty = make(map[string]*emptyFlaskWatch)
	}
	kept := activations[:0]
	for _, activation := range activations {
		if activation.DirectInput != "" {
			kept = append(kept, activation)
			continue
		}
		watch := state.empty[activation.Resource]
		if watch == nil {
			watch = &emptyFlaskWatch{}
			state.empty[activation.Resource] = watch
		}
		if watch.empty {
			continue
		}
		watch.pressedAt = now
		watch.pressedPercent = activation.Percent
		if percent, known := resourcePercent(vitals, activation.Resource); known {
			watch.pressedPercent = percent
		}
		watch.awaitingResult = true
		kept = append(kept, activation)
	}
	return kept
}

func resourcePercent(vitals Vitals, resource string) (float64, bool) {
	var pool Pool
	switch resource {
	case "life":
		pool = vitals.Life
	case "mana":
		pool = vitals.Mana
	default:
		return 0, false
	}
	if pool.Current == nil || pool.Maximum == nil || *pool.Maximum <= 0 {
		return 0, false
	}
	return min(100, max(0, *pool.Current / *pool.Maximum * 100)), true
}

func evaluate(resource string, player int, now time.Time, pool Pool, rule Rule, state *ruleState, held, force bool) *Activation {
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
	if !force && !emergencyEdge && state.latched && !rule.Sustained {
		return nil
	}
	if !force && !emergencyEdge && !state.last.IsZero() && now.Sub(state.last) < rule.Cooldown {
		return nil
	}
	reason := "threshold"
	if force {
		reason = "ward_break"
	} else if emergencyEdge {
		reason = "emergency"
	} else if state.latched && rule.Sustained {
		reason = "sustained"
	}
	state.latched = true
	state.last = now
	return &Activation{Player: player, Resource: resource, Percent: percent, Reason: reason}
}

func number(value float64) *float64 { return &value }

func copyNumber(value *float64) *float64 {
	if value == nil {
		return nil
	}
	return number(*value)
}

func validRule(rule Rule) bool {
	if math.IsNaN(rule.Trigger) || math.IsInf(rule.Trigger, 0) || rule.Trigger < 0 || rule.Trigger > 100 ||
		rule.Cooldown < 0 || math.IsNaN(rule.MinMissing) || math.IsInf(rule.MinMissing, 0) || rule.MinMissing < 0 {
		return false
	}
	return rule.Emergency == nil || !math.IsNaN(*rule.Emergency) && !math.IsInf(*rule.Emergency, 0) &&
		*rule.Emergency >= 0 && *rule.Emergency <= 100
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

func validWard(ward Pool) bool {
	if ward.Current == nil {
		return ward.Maximum == nil
	}
	if math.IsNaN(*ward.Current) || math.IsInf(*ward.Current, 0) || *ward.Current < 0 {
		return false
	}
	return ward.Maximum == nil || !math.IsNaN(*ward.Maximum) && !math.IsInf(*ward.Maximum, 0) && *ward.Maximum >= 0
}
