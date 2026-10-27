package slotaudit

import (
	"math"
	"slices"
	"time"

	"partypilot/internal/telemetry"
)

// Report summarizes read-only slot observations. It deliberately has no
// reliable/unreliable verdict: only a live game check can establish that.
type Report struct {
	ProcessID           int            `json:"processId"`
	Samples             int            `json:"samples"`
	CandidateStatuses   map[string]int `json:"candidateStatuses"`
	CompleteFrames      int            `json:"completeFrames"`
	LocalWorldErrorMax  float64        `json:"localWorldErrorMax"`
	LocalGridErrorMax   float64        `json:"localGridErrorMax"`
	OrderChanges        int            `json:"orderChangesWithinArea"`
	AreaChanges         int            `json:"areaChanges"`
	SourceTimeRegressed int            `json:"sourceTimeRegressions"`
	SlotMovementFrames  [2]int         `json:"slotMovementFrames"`
	FirstSeenUTC        time.Time      `json:"firstSeenUtc"`
	LastSeenUTC         time.Time      `json:"lastSeenUtc"`
}

type prior struct {
	areaHash  uint32
	area      string
	addresses []string
	positions []*telemetry.RawSlotPosition
	seen      time.Time
	tick      int64
}

type Audit struct {
	reports map[int]*Report
	priors  map[int]prior
}

func New() *Audit { return &Audit{reports: map[int]*Report{}, priors: map[int]prior{}} }

// Observe accepts only samples already validated by telemetry.Decode.
func (a *Audit) Observe(s telemetry.Sample) {
	r := a.reports[s.ProcessID]
	if r == nil {
		r = &Report{ProcessID: s.ProcessID, CandidateStatuses: map[string]int{}}
		a.reports[s.ProcessID] = r
	}
	r.Samples++
	if r.FirstSeenUTC.IsZero() || s.SeenUTC.Before(r.FirstSeenUTC) {
		r.FirstSeenUTC = s.SeenUTC
	}
	if s.SeenUTC.After(r.LastSeenUTC) {
		r.LastSeenUTC = s.SeenUTC
	}
	status := s.LocalSlotCandidateStatus
	if s.LocalSlotCandidates != nil {
		status = s.LocalSlotCandidates.Status
	}
	if status == "" {
		status = "not_reported"
	}
	r.CandidateStatuses[status]++
	p, hasPrior := a.priors[s.ProcessID]
	if hasPrior && (s.SeenUTC.Before(p.seen) || (s.TickMS > 0 && p.tick > 0 && s.TickMS < p.tick)) {
		r.SourceTimeRegressed++
		return // An old packet cannot replace the current comparison baseline.
	}
	areaChanged := hasPrior && (s.AreaHash != p.areaHash || s.IngameDataAddress != p.area)
	if areaChanged {
		r.AreaChanges++
	}
	c := s.LocalSlotCandidates
	if c == nil {
		a.priors[s.ProcessID] = prior{areaHash: s.AreaHash, area: s.IngameDataAddress, seen: s.SeenUTC, tick: s.TickMS}
		return
	}
	if hasPrior && !areaChanged && len(p.addresses) > 0 && len(c.OrderedAddresses) > 0 &&
		!slices.Equal(p.addresses, c.OrderedAddresses) {
		r.OrderChanges++
	}
	complete := len(c.RawPositions) == len(c.OrderedAddresses) && len(c.RawPositions) > 0
	for _, position := range c.RawPositions {
		if position == nil {
			complete = false
		}
	}
	if complete {
		r.CompleteFrames++
		if local := c.ObservedLocalPositionComparison; local != nil {
			r.LocalWorldErrorMax = math.Max(r.LocalWorldErrorMax, local.WorldXYDistance)
			r.LocalGridErrorMax = math.Max(r.LocalGridErrorMax, local.GridXYDistance)
		}
		if hasPrior && !areaChanged && slices.Equal(p.addresses, c.OrderedAddresses) &&
			len(p.positions) == len(c.RawPositions) {
			for i, current := range c.RawPositions {
				if i >= len(r.SlotMovementFrames) {
					break
				}
				before := p.positions[i]
				if before != nil && math.Hypot(current.WorldX-before.WorldX,
					current.WorldY-before.WorldY) > 0 {
					r.SlotMovementFrames[i]++
				}
			}
		}
	}
	a.priors[s.ProcessID] = prior{areaHash: s.AreaHash, area: s.IngameDataAddress,
		addresses: slices.Clone(c.OrderedAddresses), positions: slices.Clone(c.RawPositions),
		seen: s.SeenUTC, tick: s.TickMS}
}

func (a *Audit) Reports() []Report {
	ids := make([]int, 0, len(a.reports))
	for id := range a.reports {
		ids = append(ids, id)
	}
	slices.Sort(ids)
	result := make([]Report, 0, len(ids))
	for _, id := range ids {
		result = append(result, *a.reports[id])
	}
	return result
}
