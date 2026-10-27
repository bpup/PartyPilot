package slotaudit

import (
	"math"
	"slices"
	"time"

	"partypilot/internal/live"
	"partypilot/internal/telemetry"
)

// Report summarizes read-only slot observations. It deliberately has no
// reliable/unreliable verdict: only a live game check can establish that.
type Report struct {
	ProcessID           int               `json:"processId"`
	Samples             int               `json:"samples"`
	CandidateStatuses   map[string]int    `json:"candidateStatuses"`
	CompleteFrames      int               `json:"completeFrames"`
	RawPathFrames       int               `json:"rawPathFrames"`
	LocalWorldErrorMax  float64           `json:"localWorldErrorMax"`
	LocalGridErrorMax   float64           `json:"localGridErrorMax"`
	OrderChanges        int               `json:"orderChangesWithinArea"`
	AreaChanges         int               `json:"areaChanges"`
	SourceTimeRegressed int               `json:"sourceTimeRegressions"`
	SlotMovementFrames  [2]int            `json:"slotMovementFrames"`
	FirstSeenUTC        time.Time         `json:"firstSeenUtc"`
	LastSeenUTC         time.Time         `json:"lastSeenUtc"`
	FastMotion          *FastMotionReport `json:"fastMotion,omitempty"`
}

// FastMotionReport contains measurements only. A nearby ordinary sample does
// not establish that either local slot belongs to the intended game player.
type FastMotionReport struct {
	Frames                int           `json:"frames"`
	CompleteFrames        int           `json:"completeFrames"`
	AreaChanges           int           `json:"areaChanges"`
	SourceTimeRegressions int           `json:"sourceTimeRegressions"`
	SlotMovementFrames    [2]int        `json:"slotMovementFrames"`
	OrdinaryMatches       int           `json:"ordinaryMatches"`
	UnmappedLocalFrames   int           `json:"unmappedLocalFrames"`
	NearestGapMax         time.Duration `json:"nearestGapMaxNanoseconds"`
	ObservedWorldErrorMax float64       `json:"observedWorldErrorMax"`
}

type ordinaryAnchor struct {
	seen      time.Time
	areaHash  uint32
	localSlot int
	x, y      float64
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
	reports  map[int]*Report
	priors   map[int]prior
	ordinary map[int][]ordinaryAnchor
	fast     map[int][]live.OrderedClientFrame
}

func New() *Audit {
	return &Audit{reports: map[int]*Report{}, priors: map[int]prior{},
		ordinary: map[int][]ordinaryAnchor{}, fast: map[int][]live.OrderedClientFrame{}}
}

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
	if c := s.LocalSlotCandidates; c != nil && s.ObservedLocalAddress != "" &&
		s.CoordinateSpace == "world" {
		for i, address := range c.OrderedAddresses {
			if address == s.ObservedLocalAddress && i < 2 {
				a.ordinary[s.ProcessID] = append(a.ordinary[s.ProcessID],
					ordinaryAnchor{s.SeenUTC, s.AreaHash, i + 1, s.X, s.Y})
				break
			}
		}
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
		if c.RawPathEntries > 0 {
			r.RawPathFrames++
		}
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

// ObserveFast accepts only frames from the isolated, unverified candidate
// producer. It never changes live control or marks a slot reliable.
func (a *Audit) ObserveFast(f live.OrderedClientFrame) bool {
	if f.ProcessID <= 0 || f.SeenUTC.IsZero() || f.PlayerOrderReliable ||
		f.PlayerOrderSource != "unverified_exilecore_local_slot_candidate" ||
		f.CoordinateSpace != "world" || len(f.Players) > 2 {
		return false
	}
	a.fast[f.ProcessID] = append(a.fast[f.ProcessID], f)
	return true
}

func summarizeFast(frames []live.OrderedClientFrame, anchors []ordinaryAnchor) *FastMotionReport {
	r := &FastMotionReport{Frames: len(frames)}
	byArea := map[uint32][]ordinaryAnchor{}
	for _, anchor := range anchors {
		byArea[anchor.areaHash] = append(byArea[anchor.areaHash], anchor)
	}
	for area := range byArea {
		slices.SortFunc(byArea[area], func(a, b ordinaryAnchor) int { return a.seen.Compare(b.seen) })
	}
	for i, f := range frames {
		if i > 0 {
			p := frames[i-1]
			if f.AreaHash != p.AreaHash {
				r.AreaChanges++
			}
			if f.SeenUTC.Before(p.SeenUTC) || (f.TickMS > 0 && p.TickMS > 0 && f.TickMS < p.TickMS) {
				r.SourceTimeRegressions++
			}
		}
		if len(f.Players) != 2 || f.Players[0].LocalPlayer != 1 ||
			f.Players[1].LocalPlayer != 2 || !finitePlayers(f.Players) {
			continue
		}
		r.CompleteFrames++
		if i > 0 && f.AreaHash == frames[i-1].AreaHash &&
			!f.SeenUTC.Before(frames[i-1].SeenUTC) && f.TickMS >= frames[i-1].TickMS &&
			len(frames[i-1].Players) == 2 &&
			frames[i-1].Players[0].LocalPlayer == 1 && frames[i-1].Players[1].LocalPlayer == 2 {
			for slot := 0; slot < 2; slot++ {
				p := frames[i-1].Players[slot]
				if math.Hypot(f.Players[slot].X-p.X, f.Players[slot].Y-p.Y) > 0 {
					r.SlotMovementFrames[slot]++
				}
			}
		}
		areaAnchors := byArea[f.AreaHash]
		index, _ := slices.BinarySearchFunc(areaAnchors, f.SeenUTC,
			func(a ordinaryAnchor, seen time.Time) int { return a.seen.Compare(seen) })
		var nearest *ordinaryAnchor
		var gap time.Duration
		for _, j := range []int{index - 1, index} {
			if j < 0 || j >= len(areaAnchors) {
				continue
			}
			candidate := &areaAnchors[j]
			d := f.SeenUTC.Sub(candidate.seen)
			if d < 0 {
				d = -d
			}
			if nearest == nil || d < gap {
				nearest, gap = candidate, d
			}
		}
		if nearest == nil {
			r.UnmappedLocalFrames++
			continue
		}
		r.OrdinaryMatches++
		if gap > r.NearestGapMax {
			r.NearestGapMax = gap
		}
		p := f.Players[nearest.localSlot-1]
		r.ObservedWorldErrorMax = math.Max(r.ObservedWorldErrorMax,
			math.Hypot(p.X-nearest.x, p.Y-nearest.y))
	}
	return r
}

func finitePlayers(players []live.OrderedLocalPlayer) bool {
	for _, p := range players {
		if math.IsNaN(p.X) || math.IsInf(p.X, 0) || math.IsNaN(p.Y) || math.IsInf(p.Y, 0) {
			return false
		}
	}
	return true
}

func (a *Audit) Reports() []Report {
	for id, frames := range a.fast {
		r := a.reports[id]
		if r == nil {
			r = &Report{ProcessID: id, CandidateStatuses: map[string]int{}}
			a.reports[id] = r
		}
		r.FastMotion = summarizeFast(frames, a.ordinary[id])
	}
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
