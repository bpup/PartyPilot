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
	engine   *flask.Engine
	pidSlot  map[int]int
	pidRoles map[int][]RoleBinding
	live     *Engine
	menuOpen map[int]bool
	seen     map[int]time.Time
	maxAge   time.Duration
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
	if sample.Life == nil || sample.LifeMax == nil || *sample.Life <= 0 || *sample.LifeMax <= 0 ||
		sample.Mana == nil || sample.ManaMax == nil || *sample.ManaMax <= 0 {
		return nil, nil
	}
	rules, err := flask.ResetProfiles(slot)
	if err != nil {
		return nil, err
	}
	data := flask.Vitals{
		Life:         flask.Pool{Current: sample.Life, Maximum: sample.LifeMax},
		Mana:         flask.Pool{Current: sample.Mana, Maximum: sample.ManaMax},
		EnergyShield: flask.Pool{Current: sample.EnergyShield, Maximum: sample.EnergyShieldMax},
	}
	// The target's live call passes recovering=false; Ward has no verified live source.
	return session.engine.Step(slot, sample.SeenUTC, data, rules, session.menuOpen[slot], false)
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
			player.Mana == nil || player.ManaMax == nil ||
			!validOrderedPool(player.Life, player.LifeMax) || *player.LifeMax <= 0 ||
			!validOrderedPool(player.Mana, player.ManaMax) || *player.ManaMax <= 0 ||
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
		rules, err := flask.ResetProfiles(role.Slot)
		if err != nil {
			return nil, err
		}
		data := flask.Vitals{
			Life:         flask.Pool{Current: player.Life, Maximum: player.LifeMax},
			Mana:         flask.Pool{Current: player.Mana, Maximum: player.ManaMax},
			EnergyShield: flask.Pool{Current: player.EnergyShield, Maximum: player.EnergyShieldMax},
		}
		requested, err := session.engine.Step(role.Slot, frame.SeenUTC,
			data, rules, session.menuOpen[role.Slot], false)
		if err != nil {
			return nil, err
		}
		activations = append(activations, requested...)
	}
	return activations, nil
}
