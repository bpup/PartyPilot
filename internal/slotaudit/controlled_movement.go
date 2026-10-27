package slotaudit

import (
	"math"
	"time"

	"partypilot/internal/live"
)

// MovementWindow is relative to the first fast frame for each process.
// The caller labels which player was intentionally moved; measurements alone
// never establish player identity or authorize live control.
type MovementWindow struct {
	Label string        `json:"label"`
	Start time.Duration `json:"startNanoseconds"`
	End   time.Duration `json:"endNanoseconds"`
}

type MovementWindowReport struct {
	Label             string     `json:"label"`
	StartSeconds      float64    `json:"startSeconds"`
	EndSeconds        float64    `json:"endSeconds"`
	CompleteFrames    int        `json:"completeFrames"`
	SameAreaSteps     int        `json:"sameAreaSteps"`
	AreaChanges       int        `json:"areaChanges"`
	TimeRegressions   int        `json:"timeRegressions"`
	PathDistanceWorld [2]float64 `json:"pathDistanceWorld"`
}

func (a *Audit) ControlledMovement(windows []MovementWindow) map[int][]MovementWindowReport {
	result := make(map[int][]MovementWindowReport, len(a.fast))
	for processID, frames := range a.fast {
		if len(frames) == 0 {
			continue
		}
		start := frames[0].SeenUTC
		for _, window := range windows {
			report := MovementWindowReport{Label: window.Label,
				StartSeconds: window.Start.Seconds(), EndSeconds: window.End.Seconds()}
			var previous *live.OrderedClientFrame
			for index := range frames {
				frame := &frames[index]
				elapsed := frame.SeenUTC.Sub(start)
				if elapsed < window.Start || elapsed >= window.End || !completeTwoSlots(*frame) {
					continue
				}
				report.CompleteFrames++
				if previous != nil {
					if frame.SeenUTC.Before(previous.SeenUTC) || frame.TickMS < previous.TickMS {
						report.TimeRegressions++
					} else if frame.AreaHash != previous.AreaHash {
						report.AreaChanges++
					} else {
						report.SameAreaSteps++
						for slot := 0; slot < 2; slot++ {
							report.PathDistanceWorld[slot] += math.Hypot(
								frame.Players[slot].X-previous.Players[slot].X,
								frame.Players[slot].Y-previous.Players[slot].Y)
						}
					}
				}
				previous = frame
			}
			result[processID] = append(result[processID], report)
		}
	}
	return result
}

func completeTwoSlots(frame live.OrderedClientFrame) bool {
	return len(frame.Players) == 2 && frame.Players[0].LocalPlayer == 1 &&
		frame.Players[1].LocalPlayer == 2 && finitePlayers(frame.Players)
}
