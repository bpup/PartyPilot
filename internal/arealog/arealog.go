package arealog

import (
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"time"
)

var generation = regexp.MustCompile(`(?i)Generating\s+level\s+[0-9]+\s+area\s+"([^"]+)"\s+with\s+seed\s+([0-9]+)`)

type Signal struct {
	Slot       int       `json:"slot"`
	AreaID     string    `json:"area_id"`
	Seed       int64     `json:"seed"`
	ObservedAt time.Time `json:"observed_at"`
	Origin     string    `json:"origin"`
}

type Comparison struct {
	LeaderSlot   int    `json:"leader_slot"`
	FollowerSlot int    `json:"follower_slot"`
	Status       string `json:"status"`
	AreaID       string `json:"area_id,omitempty"`
	Seed         int64  `json:"seed,omitempty"`
}

// ParseGeneration extracts only complete generation identities. Scene labels are
// deliberately excluded because a label alone cannot distinguish instances.
func ParseGeneration(line string, slot int, observedAt time.Time) (Signal, bool) {
	match := generation.FindStringSubmatch(line)
	if match == nil {
		return Signal{}, false
	}
	seed, err := strconv.ParseInt(match[2], 10, 64)
	area := strings.TrimSpace(match[1])
	if err != nil || seed <= 0 || area == "" || slot < 1 || slot > 6 {
		return Signal{}, false
	}
	return Signal{Slot: slot, AreaID: area, Seed: seed, ObservedAt: observedAt, Origin: "live"}, true
}

func Compare(now time.Time, maxAge time.Duration, leaderSlot, followerSlot int, leader, follower *Signal) Comparison {
	result := Comparison{LeaderSlot: leaderSlot, FollowerSlot: followerSlot, Status: "missing"}
	if leader == nil || follower == nil {
		return result
	}
	if maxAge <= 0 || !fresh(now, leader.ObservedAt, maxAge) || !fresh(now, follower.ObservedAt, maxAge) {
		result.Status = "stale"
		return result
	}
	if leader.AreaID == "" || follower.AreaID == "" || leader.Seed <= 0 || follower.Seed <= 0 ||
		!strings.EqualFold(leader.AreaID, follower.AreaID) || leader.Seed != follower.Seed {
		result.Status = "different_area"
		return result
	}
	result.Status = "candidate_same_instance"
	if leader.Origin == "history" || follower.Origin == "history" {
		result.Status = "historical_candidate"
	}
	result.AreaID = leader.AreaID
	result.Seed = leader.Seed
	return result
}

func fresh(now, seen time.Time, maxAge time.Duration) bool {
	return !seen.IsZero() && !seen.After(now) && now.Sub(seen) <= maxAge
}

func ValidatePaths(paths map[int]string) error {
	return validatePaths(paths, 2, 6)
}

// ValidateClientPaths checks one log per game process, including a single
// process that hosts two local players.
func ValidateClientPaths(paths map[int]string) error {
	return validatePaths(paths, 1, 3)
}

func validatePaths(paths map[int]string, minCount, maxCount int) error {
	if len(paths) < minCount || len(paths) > maxCount {
		return fmt.Errorf("configure %d–%d distinct logs", minCount, maxCount)
	}
	seen := make(map[string]int)
	files := make(map[int]os.FileInfo)
	for slot, path := range paths {
		if slot < 1 || slot > 6 || path == "" {
			return fmt.Errorf("invalid player slot or path")
		}
		absolute, err := filepath.Abs(path)
		if err != nil {
			return err
		}
		resolved, err := filepath.EvalSymlinks(absolute)
		if err != nil {
			return fmt.Errorf("slot %d log: %w", slot, err)
		}
		key := strings.ToLower(filepath.Clean(resolved))
		if prior, exists := seen[key]; exists {
			return fmt.Errorf("slots %d and %d share a log", prior, slot)
		}
		seen[key] = slot
		info, err := os.Stat(resolved)
		if err != nil || !info.Mode().IsRegular() {
			return fmt.Errorf("slot %d log must be a regular file", slot)
		}
		for prior, previous := range files {
			if os.SameFile(info, previous) {
				return fmt.Errorf("slots %d and %d share a log", prior, slot)
			}
		}
		files[slot] = info
	}
	return nil
}
