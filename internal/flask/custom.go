package flask

import (
	"errors"
	"fmt"
	"math"
	"strings"
	"time"
)

// CustomRule carries the 0.6.17 custom recovery controls. ID is a stable
// per-rule identity for the target's independent cooldown key.
type CustomRule struct {
	ID              string  `json:"id"`
	Enabled         bool    `json:"enabled"`
	Player          string  `json:"player"`
	Resource        string  `json:"resource"`
	Comparator      string  `json:"comparator"`
	Threshold       float64 `json:"threshold"`
	CooldownSeconds float64 `json:"cooldown_seconds"`
	Priority        int     `json:"priority"`
	MinDeficit      int     `json:"min_deficit"`
	Dependency      string  `json:"dependency"`
	CombatOnly      bool    `json:"combat_only"`
	Input           string  `json:"input"`
}

func ValidateCustomRules(rules []CustomRule) error {
	seen := make(map[string]bool, len(rules))
	for _, rule := range rules {
		if strings.TrimSpace(rule.ID) == "" || seen[rule.ID] ||
			!customTargetKnown(rule.Player) || !oneOf(rule.Resource, "Life", "Mana", "Energy Shield", "Ward") ||
			!oneOf(rule.Comparator, "≤", "≥") ||
			math.IsNaN(rule.Threshold) || math.IsInf(rule.Threshold, 0) || rule.Threshold < 0 || rule.Threshold > 100 ||
			math.IsNaN(rule.CooldownSeconds) || math.IsInf(rule.CooldownSeconds, 0) ||
			rule.CooldownSeconds < 0.25 || rule.CooldownSeconds > 60 ||
			rule.Priority < 0 || rule.Priority > 100 || rule.MinDeficit < 0 ||
			!oneOf(rule.Dependency, "None", "Ward active", "Ward broken", "ES present", "ES depleted") ||
			!oneOf(rule.Input, "D-pad Left", "D-pad Right", "X", "Y", "LB", "RB", "LT", "RT", "D-pad Up", "D-pad Down") {
			return errors.New("invalid custom recovery rule")
		}
		seen[rule.ID] = true
	}
	return nil
}

func oneOf(value string, options ...string) bool {
	for _, option := range options {
		if value == option {
			return true
		}
	}
	return false
}

func customTargetKnown(target string) bool {
	return oneOf(target, "Off", "P1", "P2", "Both", "P3", "P4", "Client 2 Pair",
		"P5", "P6", "Client 3 Pair", "All Four", "All Six")
}

func customTargetsPlayer(target string, player int) bool {
	switch target {
	case "P1", "P2", "P3", "P4", "P5", "P6":
		return target == fmt.Sprintf("P%d", player)
	case "Both":
		return player <= 2
	case "Client 2 Pair":
		return player >= 3 && player <= 4
	case "Client 3 Pair":
		return player >= 5 && player <= 6
	case "All Four":
		return player <= 4
	case "All Six":
		return player <= 6
	default:
		return false
	}
}

func (state *playerState) evaluateCustom(player int, now time.Time, vitals Vitals,
	rules []CustomRule, inCombat, wardBroke bool) []Activation {
	var activations []Activation
	for _, rule := range rules {
		if !rule.Enabled || rule.CombatOnly && !inCombat || !customTargetsPlayer(rule.Player, player) {
			continue
		}
		pool := vitals.Life
		switch rule.Resource {
		case "Mana":
			pool = vitals.Mana
		case "Energy Shield":
			pool = vitals.EnergyShield
		case "Ward":
			pool = vitals.Ward
		}
		if pool.Current == nil || pool.Maximum == nil || *pool.Maximum <= 0 ||
			*pool.Maximum-*pool.Current < float64(rule.MinDeficit) {
			continue
		}
		dependencyMet := true
		switch rule.Dependency {
		case "Ward active":
			dependencyMet = vitals.Ward.Current != nil && *vitals.Ward.Current > 0
		case "Ward broken":
			dependencyMet = wardBroke
		case "ES present":
			dependencyMet = vitals.EnergyShield.Current != nil && *vitals.EnergyShield.Current > 0
		case "ES depleted":
			dependencyMet = vitals.EnergyShield.Current != nil && *vitals.EnergyShield.Current <= 0
		}
		percent := *pool.Current / *pool.Maximum * 100
		thresholdMet := rule.Comparator == "≤" && percent <= rule.Threshold ||
			rule.Comparator == "≥" && percent >= rule.Threshold
		if !dependencyMet || !thresholdMet {
			continue
		}
		if last, exists := state.customLast[rule.ID]; exists &&
			now.Sub(last) < time.Duration(rule.CooldownSeconds*float64(time.Second)) {
			continue
		}
		if state.customLast == nil {
			state.customLast = make(map[string]time.Time)
		}
		state.customLast[rule.ID] = now
		resource := "action"
		input := rule.Input
		switch input {
		case "D-pad Left":
			resource, input = "life", ""
		case "D-pad Right":
			resource, input = "mana", ""
		}
		activations = append(activations, Activation{Player: player, Resource: resource,
			Percent: percent, Reason: fmt.Sprintf("custom %s %s%.0f%% · priority %d",
				rule.Resource, rule.Comparator, rule.Threshold, rule.Priority), DirectInput: input})
	}
	return activations
}
