package live

import (
	"errors"
	"strings"
	"time"

	"partypilot/internal/flask"
	"partypilot/internal/telemetry"
)

// FlaskSession evaluates explicitly bound characters. A menu state must be
// supplied for each role before any dose is requested.
type FlaskSession struct {
	engine         *flask.Engine
	pidSlot        map[int]int
	pidRoles       map[int][]RoleBinding
	live           *Engine
	menuOpen       map[int]bool
	seen           map[int]time.Time
	maxAge         time.Duration
	custom         []flask.CustomRule
	combatAt       map[int]time.Time
	support        []FlaskSupportBinding
	supportOptions map[int]FlaskSupportBinding
	ownRules       map[int]flask.Settings
}

func (session *FlaskSession) ConfigureCustomRules(rules []flask.CustomRule) error {
	if err := flask.ValidateCustomRules(rules); err != nil {
		return err
	}
	session.custom = append([]flask.CustomRule(nil), rules...)
	return nil
}

// ObserveMainAttack records only a validated, unblocked input for a bound role.
// The target's combat-only custom rules use a 20-second recent attack window.
func (session *FlaskSession) ObserveMainAttack(slot int, observedAt time.Time, active bool) error {
	if _, bound := session.menuOpen[slot]; !bound || observedAt.IsZero() {
		return errors.New("combat input refers to an unbound player or invalid time")
	}
	if !active {
		return nil
	}
	if session.combatAt == nil {
		session.combatAt = make(map[int]time.Time)
	}
	if observedAt.After(session.combatAt[slot]) {
		session.combatAt[slot] = observedAt
	}
	return nil
}

func (session *FlaskSession) combatRecent(slot int, now time.Time) bool {
	last := session.combatAt[slot]
	return !last.IsZero() && !now.Before(last) && now.Sub(last) <= 20*time.Second
}

type FlaskSupportBinding struct {
	DrinkerPlayer  int                  `json:"drinker_player"`
	SupportBuild   bool                 `json:"support_build"`
	BuildHasNoMana bool                 `json:"build_has_no_mana"`
	SustainedUse   bool                 `json:"sustained_use"`
	Monitor        flask.SupportMonitor `json:"monitor"`
}

func (session *FlaskSession) ConfigureSupport(bindings []FlaskSupportBinding) error {
	if len(bindings) > 30 {
		return errors.New("too many flask support bindings")
	}
	seen := make(map[[2]int]bool)
	options := make(map[int]FlaskSupportBinding)
	for _, binding := range bindings {
		target := binding.Monitor.TargetPlayer
		if _, exists := session.menuOpen[binding.DrinkerPlayer]; !exists ||
			binding.DrinkerPlayer == target {
			return errors.New("support drinker or target is not a different bound player")
		}
		if _, exists := session.menuOpen[target]; !exists {
			return errors.New("support target is not bound")
		}
		if err := flask.ValidateSupportMonitor(binding.Monitor); err != nil {
			return err
		}
		key := [2]int{binding.DrinkerPlayer, target}
		if seen[key] {
			return errors.New("duplicate support drinker and target")
		}
		if previous, exists := options[binding.DrinkerPlayer]; exists &&
			(previous.SupportBuild != binding.SupportBuild ||
				previous.BuildHasNoMana != binding.BuildHasNoMana ||
				previous.SustainedUse != binding.SustainedUse) {
			return errors.New("inconsistent drinker flask settings")
		}
		options[binding.DrinkerPlayer] = binding
		seen[key] = true
	}
	session.support = append([]FlaskSupportBinding(nil), bindings...)
	session.supportOptions = options
	return nil
}

func (session *FlaskSession) rulesFor(slot int) (flask.Settings, error) {
	rules, exists := session.ownRules[slot]
	if !exists {
		var err error
		rules, err = flask.ResetProfiles(slot)
		if err != nil {
			return flask.Settings{}, err
		}
	}
	if option, exists := session.supportOptions[slot]; exists {
		rules.SupportBuild = option.SupportBuild
		rules.BuildHasNoMana = option.BuildHasNoMana
		rules.SustainedUse = option.SustainedUse
	}
	return rules, nil
}

// NewOrderedFlaskSession binds each local role separately while retaining one
// verified process identity per client. Menu state starts unknown for every role.
func NewOrderedFlaskSession(liveEngine *Engine, maxAge time.Duration) (*FlaskSession, error) {
	if liveEngine == nil || len(liveEngine.pidToRoles) == 0 || maxAge <= 0 {
		return nil, errors.New("ordered flasks require bound roles and positive sample age")
	}
	session := &FlaskSession{engine: flask.New(), pidRoles: liveEngine.pidToRoles, live: liveEngine,
		menuOpen: make(map[int]bool), seen: make(map[int]time.Time), maxAge: maxAge}
	for _, roles := range session.pidRoles {
		for _, role := range roles {
			session.menuOpen[role.Slot] = true
		}
	}
	return session, nil
}

func NewFlaskSession(liveEngine *Engine, maxAge time.Duration) (*FlaskSession, error) {
	if liveEngine == nil || len(liveEngine.pidToRoles) < 1 ||
		len(liveEngine.pidToRoles) > 3 || maxAge <= 0 {
		return nil, errors.New("flasks require 1–3 bound clients and positive sample age")
	}
	session := &FlaskSession{engine: flask.New(), pidSlot: make(map[int]int),
		live: liveEngine, menuOpen: make(map[int]bool), seen: make(map[int]time.Time),
		maxAge: maxAge}
	for pid, roles := range liveEngine.pidToRoles {
		if pid <= 0 || len(roles) != 1 || roles[0].Slot < 1 || roles[0].Slot > 6 {
			return nil, errors.New("separate-client flasks require one bound role per process")
		}
		slot := roles[0].Slot
		session.pidSlot[pid] = slot
		session.menuOpen[slot] = true // Unknown is held until the bridge reports a state.
	}
	return session, nil
}

func (session *FlaskSession) SetMenuGuard(slot int, state string) error {
	if _, exists := session.menuOpen[slot]; !exists {
		return errors.New("menu state refers to an unbound player")
	}
	switch state {
	case "clear":
		session.menuOpen[slot] = false
	case "menu_detected", "settling":
		session.menuOpen[slot] = true
	default:
		return errors.New("invalid menu state")
	}
	return nil
}

func (session *FlaskSession) ObserveSample(sample telemetry.Sample, now time.Time) ([]flask.Activation, error) {
	slot := session.pidSlot[sample.ProcessID]
	if slot == 0 || now.IsZero() || sample.SeenUTC.IsZero() || sample.SeenUTC.After(now) ||
		now.Sub(sample.SeenUTC) > session.maxAge ||
		(!session.seen[slot].IsZero() && !sample.SeenUTC.After(session.seen[slot])) {
		return nil, nil
	}
	session.seen[slot] = sample.SeenUTC
	if !session.live.flaskActionReady(slot, now) {
		return nil, nil
	}
	if sample.Life == nil || sample.LifeMax == nil || *sample.Life <= 0 || *sample.LifeMax <= 0 {
		return nil, nil
	}
	rules, err := session.rulesFor(slot)
	if err != nil {
		return nil, err
	}
	data := flask.Vitals{
		Life:         flask.Pool{Current: sample.Life, Maximum: sample.LifeMax},
		Mana:         flask.Pool{Current: sample.Mana, Maximum: sample.ManaMax},
		EnergyShield: flask.Pool{Current: sample.EnergyShield, Maximum: sample.EnergyShieldMax},
	}
	// The target's local and distributed live calls pass recovering=false;
	// Ward has no verified live source here.
	activations, err := session.engine.StepWithCustom(slot, sample.SeenUTC, data, rules,
		session.menuOpen[slot], false, session.custom, session.combatRecent(slot, now))
	if err != nil {
		return nil, err
	}
	support, err := session.evaluateSupport(now, slot)
	return append(activations, support...), err
}

// ObserveOrderedVitals evaluates a complete slow frame only after every local
// role and every flask resource has been checked. It does not use motion data.
func (session *FlaskSession) ObserveOrderedVitals(frame OrderedVitalsFrame,
	now time.Time) ([]flask.Activation, error) {
	roles := session.pidRoles[frame.ProcessID]
	if len(roles) == 0 || !frame.PlayerOrderReliable ||
		strings.TrimSpace(frame.PlayerOrderSource) == "" || len(frame.Players) != len(roles) ||
		now.IsZero() || frame.SeenUTC.IsZero() || frame.SeenUTC.After(now) ||
		now.Sub(frame.SeenUTC) > session.maxAge ||
		!session.live.orderedVitalActionReady(frame.ProcessID, now) {
		return nil, nil
	}
	byLocal := make(map[int]OrderedLocalVitals, len(frame.Players))
	for _, player := range frame.Players {
		if player.LocalPlayer < 1 || player.LocalPlayer > len(roles) ||
			player.Life == nil || player.LifeMax == nil ||
			!validOrderedPool(player.Life, player.LifeMax) || *player.LifeMax <= 0 ||
			!validOrderedPool(player.Mana, player.ManaMax) ||
			!validOrderedPool(player.EnergyShield, player.EnergyShieldMax) {
			return nil, nil
		}
		if _, duplicate := byLocal[player.LocalPlayer]; duplicate {
			return nil, nil
		}
		byLocal[player.LocalPlayer] = player
	}
	for _, role := range roles {
		if _, exists := byLocal[role.LocalPlayer]; !exists ||
			!session.seen[role.Slot].IsZero() &&
				!frame.SeenUTC.After(session.seen[role.Slot]) {
			return nil, nil
		}
	}
	var activations []flask.Activation
	for _, role := range roles {
		player := byLocal[role.LocalPlayer]
		session.seen[role.Slot] = frame.SeenUTC
		if *player.Life <= 0 {
			continue
		}
		rules, err := session.rulesFor(role.Slot)
		if err != nil {
			return nil, err
		}
		data := flask.Vitals{
			Life:         flask.Pool{Current: player.Life, Maximum: player.LifeMax},
			Mana:         flask.Pool{Current: player.Mana, Maximum: player.ManaMax},
			EnergyShield: flask.Pool{Current: player.EnergyShield, Maximum: player.EnergyShieldMax},
		}
		requested, err := session.engine.StepWithCustom(role.Slot, frame.SeenUTC,
			data, rules, session.menuOpen[role.Slot], false, session.custom,
			session.combatRecent(role.Slot, now))
		if err != nil {
			return nil, err
		}
		activations = append(activations, requested...)
	}
	support, err := session.evaluateSupport(now, 0)
	return append(activations, support...), err
}

func (session *FlaskSession) evaluateSupport(now time.Time, changedSlot int) ([]flask.Activation, error) {
	var activations []flask.Activation
	for _, binding := range session.support {
		target := binding.Monitor.TargetPlayer
		if changedSlot != 0 && changedSlot != binding.DrinkerPlayer && changedSlot != target {
			continue
		}
		if !session.live.flaskSupportPairReady(binding.DrinkerPlayer, target, now) {
			continue
		}
		targetVital := session.live.vitals[target]
		rules, err := session.rulesFor(binding.DrinkerPlayer)
		if err != nil {
			return nil, err
		}
		data := flask.Vitals{
			Life: flask.Pool{Current: targetVital.Life, Maximum: targetVital.LifeMax},
			Mana: flask.Pool{Current: targetVital.Mana, Maximum: targetVital.ManaMax},
			EnergyShield: flask.Pool{Current: targetVital.EnergyShield,
				Maximum: targetVital.EnergyShieldMax},
		}
		requested, err := session.engine.StepSupport(binding.DrinkerPlayer, now, data,
			rules, binding.Monitor, session.menuOpen[binding.DrinkerPlayer])
		if err != nil {
			return nil, err
		}
		for _, activation := range requested {
			activation.Reason = "support" // The desktop wire carries a bounded reason enum.
			activations = append(activations, activation)
		}
	}
	return activations, nil
}
