package live

import (
	"errors"
	"math"
	"time"

	"partypilot/internal/flask"
)

// FlaskRuleBinding contains the target's per-player life, mana and available
// Energy Shield controls for the current live sensor path.
type FlaskRuleBinding struct {
	Player           int     `json:"player"`
	LifeEnabled      bool    `json:"life_enabled"`
	LifeTrigger      float64 `json:"life_trigger"`
	LifeCooldownMS   int     `json:"life_cooldown_ms"`
	Emergency        float64 `json:"emergency"`
	LifeMinMissing   int     `json:"life_min_missing"`
	SkipRecovering   bool    `json:"skip_recovering"`
	ManaEnabled      bool    `json:"mana_enabled"`
	ManaTrigger      float64 `json:"mana_trigger"`
	ManaCooldownMS   int     `json:"mana_cooldown_ms"`
	ManaMinMissing   int     `json:"mana_min_missing"`
	ChaosInoculation bool    `json:"chaos_inoculation"`
	CombinedDefence  bool    `json:"combined_defence"`
}

func (session *FlaskSession) ConfigureRules(bindings []FlaskRuleBinding) error {
	if len(bindings) != len(session.menuOpen) {
		return errors.New("flask rules require every bound player")
	}
	configured := make(map[int]flask.Settings, len(bindings))
	for _, binding := range bindings {
		if _, exists := session.menuOpen[binding.Player]; !exists {
			return errors.New("flask rules refer to an unbound player")
		}
		if _, duplicate := configured[binding.Player]; duplicate {
			return errors.New("duplicate player flask rules")
		}
		if !boundedPercent(binding.LifeTrigger) || !boundedPercent(binding.ManaTrigger) ||
			!boundedPercent(binding.Emergency) ||
			binding.LifeCooldownMS < 100 || binding.LifeCooldownMS > 60000 ||
			binding.ManaCooldownMS < 100 || binding.ManaCooldownMS > 60000 ||
			binding.LifeMinMissing < 0 || binding.LifeMinMissing > 10000 ||
			binding.ManaMinMissing < 0 || binding.ManaMinMissing > 10000 {
			return errors.New("flask rule is outside target controls")
		}
		rules, err := flask.ResetProfiles(binding.Player)
		if err != nil {
			return err
		}
		rules.Life.Enabled = binding.LifeEnabled
		rules.Life.Trigger = binding.LifeTrigger
		rules.Life.Cooldown = time.Duration(binding.LifeCooldownMS) * time.Millisecond
		rules.Life.Emergency = &binding.Emergency
		rules.Life.MinMissing = float64(binding.LifeMinMissing)
		rules.Life.SkipRecovering = binding.SkipRecovering
		rules.Mana.Enabled = binding.ManaEnabled
		rules.Mana.Trigger = binding.ManaTrigger
		rules.Mana.Cooldown = time.Duration(binding.ManaCooldownMS) * time.Millisecond
		rules.Mana.MinMissing = float64(binding.ManaMinMissing)
		rules.ChaosInoculation = binding.ChaosInoculation
		rules.CombinedDefence = binding.CombinedDefence
		configured[binding.Player] = rules
	}
	session.ownRules = configured
	return nil
}

func boundedPercent(value float64) bool {
	return !math.IsNaN(value) && !math.IsInf(value, 0) && value >= 1 && value <= 95
}
