package live

import (
	"errors"
	"time"

	"partypilot/internal/flask"
	"partypilot/internal/telemetry"
)

// FlaskSession only evaluates the one local character explicitly bound to each
// client process. A menu state must be supplied before any dose is requested.
type FlaskSession struct {
	engine   *flask.Engine
	pidSlot  map[int]int
	menuOpen map[int]bool
	seen     map[int]time.Time
	maxAge   time.Duration
}

func NewFlaskSession(roster map[int]int, maxAge time.Duration) (*FlaskSession, error) {
	if len(roster) < 1 || len(roster) > 3 || maxAge <= 0 {
		return nil, errors.New("flasks require 1–3 bound clients and positive sample age")
	}
	session := &FlaskSession{engine: flask.New(), pidSlot: make(map[int]int),
		menuOpen: make(map[int]bool), seen: make(map[int]time.Time), maxAge: maxAge}
	for slot, pid := range roster {
		if slot < 1 || slot > 6 || pid <= 0 || session.pidSlot[pid] != 0 {
			return nil, errors.New("invalid or duplicate flask process binding")
		}
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
