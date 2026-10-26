package combat

import (
	"sort"
	"strconv"
	"unicode/utf16"
)

// applyDefaultPacing annotates read-only candidates using the supplied
// profile's Natural reaction and Natural party timing. It schedules no input.
func applyDefaultPacing(profile Profile, encounterID string, plans []Plan) {
	roles := make(map[int]Role, len(profile.Roles))
	for _, role := range profile.Roles {
		roles[role.Player] = role
	}
	indices := make([]int, 0, len(plans))
	for index := range plans {
		if plans[index].Status == "planned_only" {
			indices = append(indices, index)
		}
	}
	sort.Slice(indices, func(i, j int) bool {
		left, right := roles[plans[indices[i]].Player], roles[plans[indices[j]].Player]
		if rolePacingOrder(left.Kind) != rolePacingOrder(right.Kind) {
			return rolePacingOrder(left.Kind) < rolePacingOrder(right.Kind)
		}
		return left.Player < right.Player
	})
	for order, index := range indices {
		role := roles[plans[index].Player]
		seed := stablePacingHash(encounterID + ":P" + strconv.Itoa(role.Player))
		offset := boundedPacing(seed, 110, 230) +
			boundedPacing(seed/7, role.RoleOffsetMinMS, role.RoleOffsetMaxMS)
		if order != 0 {
			offset += order * boundedPacing(seed/13, 45, 110)
		}
		offset = max(0, offset)
		plans[index].ScheduledOffsetMS = &offset
	}
}

func rolePacingOrder(kind string) int {
	switch kind {
	case "main-dps":
		return 0
	case "companion-dps":
		return 2
	case "support":
		return 3
	case "defence":
		return 4
	case "rarity-finisher":
		return 5
	default:
		return 8
	}
}

func stablePacingHash(value string) int {
	var hash int32 = 17
	for _, unit := range utf16.Encode([]rune(value)) {
		hash = hash*31 + int32(unit)
	}
	if hash == -1<<31 {
		return 0
	}
	if hash < 0 {
		return int(-hash)
	}
	return int(hash)
}

func boundedPacing(seed, minimum, maximum int) int {
	if maximum <= minimum {
		return minimum
	}
	remainder := seed % (maximum - minimum + 1)
	if remainder < 0 {
		remainder = -remainder
	}
	return minimum + remainder
}
