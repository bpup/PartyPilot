package live

import (
	"math"
	"time"
)

// MainInput is a controller sample relayed by the protected bridge. It is
// stored separately from position telemetry until close mirroring is enabled.
type MainInput struct {
	Slot       int
	LeftX      float64
	LeftY      float64
	BDown      bool
	Blocked    bool
	ObservedAt time.Time
}

const mainInputMaxAge = 500 * time.Millisecond

func (e *Engine) SetAcceptMainInput(enabled bool) {
	e.acceptMainInput = enabled
	if !enabled {
		e.mainInputValid = false
		e.resetCloseMirrors()
	}
}

func (e *Engine) ObserveMainInput(input MainInput, now time.Time) bool {
	if !e.acceptMainInput || now.IsZero() || input.ObservedAt.IsZero() || input.ObservedAt.After(now) ||
		now.Sub(input.ObservedAt) > mainInputMaxAge || input.Slot != e.leader ||
		math.IsNaN(input.LeftX) || math.IsInf(input.LeftX, 0) ||
		math.IsNaN(input.LeftY) || math.IsInf(input.LeftY, 0) ||
		math.Abs(input.LeftX) > 1 || math.Abs(input.LeftY) > 1 ||
		(e.mainInputValid && input.ObservedAt.Before(e.mainInput.ObservedAt)) {
		return false
	}
	e.mainInput = input
	e.mainInputValid = true
	return true
}

func (e *Engine) CurrentMainInput(now time.Time) (MainInput, bool) {
	if !e.mainInputValid || e.followPaused || e.stopped != "" ||
		e.mainInput.Slot != e.leader || now.Before(e.mainInput.ObservedAt) ||
		now.Sub(e.mainInput.ObservedAt) > mainInputMaxAge {
		return MainInput{}, false
	}
	return e.mainInput, true
}
