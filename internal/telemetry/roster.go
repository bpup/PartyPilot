package telemetry

import (
	"sort"
	"time"
)

type LeaderObservation struct {
	Slot      int    `json:"slot"`
	ProcessID int    `json:"process_id"`
	Present   bool   `json:"present"`
	Fresh     bool   `json:"fresh"`
	AreaName  string `json:"area_name,omitempty"`
	AreaHash  uint32 `json:"area_hash,omitempty"`
}

type FollowerObservation struct {
	Slot       int        `json:"slot"`
	Comparison Comparison `json:"comparison"`
}

type RosterSnapshot struct {
	Leader    LeaderObservation     `json:"leader"`
	Followers []FollowerObservation `json:"followers"`
}

// RosterMonitor never turns area/hash equality into an instance identity.
type RosterMonitor struct {
	roster     map[int]int
	pidToSlot  map[int]int
	ordered    []int
	leaderSlot int
	maxAge     time.Duration
	samples    map[int]Sample
}

func NewRosterMonitor(roster map[int]int, leaderSlot int, maxAge time.Duration) (*RosterMonitor, bool) {
	if len(roster) < 1 || len(roster) > 6 || maxAge <= 0 {
		return nil, false
	}
	if _, exists := roster[leaderSlot]; !exists {
		return nil, false
	}
	monitor := &RosterMonitor{
		roster: make(map[int]int, len(roster)), pidToSlot: make(map[int]int, len(roster)),
		leaderSlot: leaderSlot, maxAge: maxAge, samples: make(map[int]Sample),
	}
	for slot, pid := range roster {
		if slot < 1 || slot > 6 || pid <= 0 {
			return nil, false
		}
		if _, exists := monitor.pidToSlot[pid]; exists {
			return nil, false
		}
		monitor.roster[slot] = pid
		monitor.pidToSlot[pid] = slot
		monitor.ordered = append(monitor.ordered, slot)
	}
	sort.Ints(monitor.ordered)
	return monitor, true
}

func (m *RosterMonitor) Update(item Sample) bool {
	if _, exists := m.pidToSlot[item.ProcessID]; !exists || item.SeenUTC.IsZero() ||
		!finite(item.X) || !finite(item.Y) || !finite(item.Z) {
		return false
	}
	if previous, exists := m.samples[item.ProcessID]; exists && item.SeenUTC.Before(previous.SeenUTC) {
		return false
	}
	m.samples[item.ProcessID] = item
	return true
}

func (m *RosterMonitor) Snapshot(now time.Time) RosterSnapshot {
	leaderPID := m.roster[m.leaderSlot]
	leader := LeaderObservation{Slot: m.leaderSlot, ProcessID: leaderPID}
	var leaderSample *Sample
	if sample, exists := m.samples[leaderPID]; exists {
		leader.Present = true
		leader.Fresh = fresh(now, sample.SeenUTC, m.maxAge)
		if leader.Fresh {
			leader.AreaName = sample.AreaName
			leader.AreaHash = sample.AreaHash
		}
		leaderSample = &sample
	}
	snapshot := RosterSnapshot{Leader: leader, Followers: make([]FollowerObservation, 0, len(m.roster)-1)}
	for _, slot := range m.ordered {
		if slot == m.leaderSlot {
			continue
		}
		pid := m.roster[slot]
		var followerSample *Sample
		if sample, exists := m.samples[pid]; exists {
			followerSample = &sample
		}
		snapshot.Followers = append(snapshot.Followers, FollowerObservation{
			Slot:       slot,
			Comparison: compareSamples(now, leaderPID, pid, leaderSample, followerSample, m.maxAge),
		})
	}
	return snapshot
}
