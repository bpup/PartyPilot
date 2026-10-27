package live

import (
	"sort"
	"strings"
	"time"

	"partypilot/internal/arealog"
)

// HandoffRequest identifies a client that must rejoin a target in another
// instance. It is a decision only; consumers must verify the game UI before
// sending any input.
type HandoffRequest struct {
	DestinationClient int    `json:"destination_client"`
	DriverPlayer      int    `json:"driver_player"`
	TargetPlayer      int    `json:"target_player"`
	State             string `json:"state"`
}

// CrossAreaRequests reports only confirmed differences between fresh,
// log-bound client observations. It does not infer a route or click a portrait.
func (e *Engine) CrossAreaRequests(now time.Time) []HandoffRequest {
	if e.stopped != "" || e.followPaused {
		return nil
	}
	bindings := make(map[int]RoleBinding, len(e.slots))
	for _, roles := range e.pidToRoles {
		for _, role := range roles {
			bindings[role.Slot] = role
		}
	}
	requests := make(map[int]HandoffRequest)
	conflicted := make(map[int]bool)
	for slot, target := range e.coordinator.ResolvedTargets() {
		follower := bindings[slot]
		leader := bindings[target]
		if follower.Client == 0 || leader.Client == 0 || follower.Client == leader.Client {
			continue
		}
		followerArea, followerOK := e.confirmedArea(slot, now)
		leaderArea, leaderOK := e.confirmedArea(target, now)
		if !followerOK || !leaderOK ||
			strings.EqualFold(followerArea.AreaID, leaderArea.AreaID) &&
				followerArea.Seed == leaderArea.Seed {
			continue
		}
		var driver int
		for _, role := range e.pidToRoles[follower.ProcessID] {
			if role.LocalPlayer == 1 {
				driver = role.Slot
				break
			}
		}
		if driver == 0 {
			continue
		}
		request := HandoffRequest{DestinationClient: follower.Client,
			DriverPlayer: driver, TargetPlayer: target, State: "warp_requested"}
		if existing, found := requests[follower.Client]; found &&
			existing.TargetPlayer != target {
			conflicted[follower.Client] = true
		}
		requests[follower.Client] = request
	}
	clients := make([]int, 0, len(requests))
	for client := range requests {
		clients = append(clients, client)
	}
	sort.Ints(clients)
	result := make([]HandoffRequest, 0, len(clients))
	for _, client := range clients {
		if !conflicted[client] {
			result = append(result, requests[client])
		}
	}
	return result
}

func (e *Engine) confirmedArea(slot int, now time.Time) (arealog.Signal, bool) {
	area, hasArea := e.areas[slot]
	motion, hasMotion := e.samples[slot]
	if !hasArea || !hasMotion || !e.areaCurrent(slot, motion, area, now) ||
		e.areaHashes[slot] == 0 {
		return arealog.Signal{}, false
	}
	if life := e.currentLife(slot, motion, area, now); life != nil && *life <= 0 {
		return arealog.Signal{}, false
	}
	return area, true
}
