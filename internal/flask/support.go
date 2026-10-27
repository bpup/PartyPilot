package flask

import (
	"errors"
	"fmt"
	"math"
	"strings"
	"time"
)

// SupportMonitor mirrors the target's watched player and flask choices.
// The caller must supply the watched player's vitals separately.
type SupportMonitor struct {
	Enabled         bool    `json:"enabled"`
	TargetPlayer    int     `json:"target_player"`
	MonitorResource string  `json:"monitor_resource"`
	TriggerPercent  float64 `json:"trigger_percent"`
	FlaskResource   string  `json:"flask_resource"`
	QuickTopUp      bool    `json:"quick_top_up"`
}

type supportKey struct {
	drinker int
	target  int
	watch   string
	flask   string
}

// StepSupport returns decisions for the drinker's flask while observing a
// different player. It shares the drinker's life/mana cooldowns with Step.
func (engine *Engine) StepSupport(drinker int, now time.Time, targetVitals Vitals,
	rules Settings, monitor SupportMonitor, menuOpen bool) ([]Activation, error) {
	if drinker < 1 || drinker > 6 || monitor.TargetPlayer < 1 || monitor.TargetPlayer > 6 ||
		now.IsZero() || !validRule(rules.Life) || !validRule(rules.Mana) ||
		!validPool(targetVitals.Life) || !validPool(targetVitals.Mana) ||
		!validPool(targetVitals.EnergyShield) ||
		math.IsNaN(monitor.TriggerPercent) || math.IsInf(monitor.TriggerPercent, 0) ||
		monitor.TriggerPercent < 1 || monitor.TriggerPercent > 99 {
		return nil, errors.New("invalid support player, time, rule, or vitals")
	}
	watch, ok := canonicalWatch(monitor.MonitorResource)
	if !ok {
		return nil, errors.New("unknown support monitor resource")
	}
	flask, ok := canonicalFlask(monitor.FlaskResource)
	if !ok {
		return nil, errors.New("unknown support flask resource")
	}
	if !rules.SupportBuild || !monitor.Enabled || drinker == monitor.TargetPlayer {
		engine.clearSupportTarget(drinker, monitor.TargetPlayer)
		return nil, nil
	}
	threshold := monitor.TriggerPercent
	if monitor.QuickTopUp {
		watch, threshold = "Any: Life / Mana / ES", 99
	}
	percent, known := watchedPercent(targetVitals, watch, threshold)
	if !known {
		return nil, nil
	}
	if percent > threshold {
		engine.clearSupportTarget(drinker, monitor.TargetPlayer)
		return nil, nil
	}
	if menuOpen {
		return nil, nil
	}
	state := engine.players[drinker]
	if state == nil {
		state = &playerState{}
		engine.players[drinker] = state
	}
	resources := []string{strings.ToLower(flask)}
	if flask == "Both" {
		resources = []string{"life", "mana"}
	}
	var activations []Activation
	for _, resource := range resources {
		key := supportKey{drinker, monitor.TargetPlayer, watch, resource}
		selected := &state.life
		rule := rules.Life
		if resource == "mana" {
			selected, rule = &state.mana, rules.Mana
		}
		if !rule.Enabled || resource == "mana" && rules.BuildHasNoMana {
			delete(engine.support, key)
			continue
		}
		if engine.support[key] && !rules.SustainedUse {
			continue
		}
		cooldown := max(100*time.Millisecond, rule.Cooldown)
		if !selected.last.IsZero() && now.Sub(selected.last) < cooldown {
			continue
		}
		engine.support[key] = true
		selected.last = now
		activations = append(activations, Activation{Player: drinker, Resource: resource,
			Percent: percent, Reason: fmt.Sprintf("Drinker support · P%d %s ≤ %.0f%%",
				monitor.TargetPlayer, strings.ToLower(watch), threshold)})
	}
	return activations, nil
}

func (engine *Engine) clearSupportTarget(drinker, target int) {
	for key := range engine.support {
		if key.drinker == drinker && key.target == target {
			delete(engine.support, key)
		}
	}
}

func canonicalWatch(value string) (string, bool) {
	for _, option := range []string{"Life", "Mana", "Energy Shield", "Life AND Mana",
		"Life OR Mana", "Any: Life / Mana / ES"} {
		if strings.EqualFold(strings.TrimSpace(value), option) {
			return option, true
		}
	}
	return "", false
}

func canonicalFlask(value string) (string, bool) {
	for _, option := range []string{"Life", "Mana", "Both"} {
		if strings.EqualFold(strings.TrimSpace(value), option) {
			return option, true
		}
	}
	return "", false
}

func watchedPercent(vitals Vitals, watch string, threshold float64) (float64, bool) {
	pools := []Pool{vitals.Life}
	switch watch {
	case "Mana":
		pools = []Pool{vitals.Mana}
	case "Energy Shield":
		pools = []Pool{vitals.EnergyShield}
	case "Life AND Mana", "Life OR Mana":
		pools = []Pool{vitals.Life, vitals.Mana}
	case "Any: Life / Mana / ES":
		pools = []Pool{vitals.Life, vitals.Mana, vitals.EnergyShield}
	}
	minimum, maximum := math.Inf(1), math.Inf(-1)
	known, missing := 0, false
	for _, pool := range pools {
		if pool.Maximum != nil && *pool.Maximum == 0 {
			continue // The target ignores resources with no pool, such as absent ES.
		}
		if pool.Current == nil || pool.Maximum == nil || *pool.Maximum <= 0 {
			missing = true
			continue
		}
		value := *pool.Current / *pool.Maximum * 100
		minimum, maximum = min(minimum, value), max(maximum, value)
		known++
	}
	if watch == "Life AND Mana" {
		return maximum, known == 2
	}
	if known == 0 || minimum > threshold && missing {
		return 0, false
	}
	return minimum, true
}
