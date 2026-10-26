package telemetry

import (
	"math"
	"time"
)

type Status string

const (
	Missing           Status = "missing"
	Stale             Status = "stale"
	DifferentArea     Status = "different_area"
	CandidateSameArea Status = "candidate_same_area"
)

type Comparison struct {
	Status      Status  `json:"status"`
	LeaderPID   int     `json:"leader_pid"`
	FollowerPID int     `json:"follower_pid"`
	AreaName    string  `json:"area_name,omitempty"`
	AreaHash    uint32  `json:"area_hash,omitempty"`
	Distance    float64 `json:"distance,omitempty"`
}

type Monitor struct {
	leaderPID   int
	followerPID int
	maxAge      time.Duration
	leader      *Sample
	follower    *Sample
}

func NewMonitor(leaderPID, followerPID int, maxAge time.Duration) (*Monitor, bool) {
	if leaderPID <= 0 || followerPID <= 0 || leaderPID == followerPID || maxAge <= 0 {
		return nil, false
	}
	return &Monitor{leaderPID: leaderPID, followerPID: followerPID, maxAge: maxAge}, true
}

func (m *Monitor) Update(item Sample) bool {
	if item.ProcessID == m.leaderPID {
		m.leader = &item
		return true
	}
	if item.ProcessID == m.followerPID {
		m.follower = &item
		return true
	}
	return false
}

func (m *Monitor) Compare(now time.Time) Comparison {
	return compareSamples(now, m.leaderPID, m.followerPID, m.leader, m.follower, m.maxAge)
}

func compareSamples(now time.Time, leaderPID, followerPID int, leader, follower *Sample, maxAge time.Duration) Comparison {
	result := Comparison{Status: Missing, LeaderPID: leaderPID, FollowerPID: followerPID}
	if leader == nil || follower == nil {
		return result
	}
	if !fresh(now, leader.SeenUTC, maxAge) || !fresh(now, follower.SeenUTC, maxAge) {
		result.Status = Stale
		return result
	}
	if leader.AreaName == "" || follower.AreaName == "" ||
		leader.AreaHash == 0 || follower.AreaHash == 0 ||
		leader.AreaName != follower.AreaName ||
		leader.AreaHash != follower.AreaHash {
		result.Status = DifferentArea
		return result
	}
	result.Status = CandidateSameArea
	result.AreaName = leader.AreaName
	result.AreaHash = leader.AreaHash
	result.Distance = math.Hypot(leader.X-follower.X, leader.Y-follower.Y)
	return result
}

func fresh(now, seen time.Time, maxAge time.Duration) bool {
	return !seen.After(now) && now.Sub(seen) <= maxAge
}
