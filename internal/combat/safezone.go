package combat

import "strings"

// IsKnownSafeZone mirrors the supplied 0.6.17 area-name rule. A false result
// means the rule did not identify a safe zone; it is not proof of combat safety.
func IsKnownSafeZone(areaID, label string) bool {
	id := strings.ToLower(areaID)
	name := strings.ToLower(label)
	for _, token := range []string{"delirium", "simulacrum"} {
		if strings.Contains(id, token) || strings.Contains(name, token) {
			return false
		}
	}
	for _, token := range []string{"hideout", "town", "encampment", "refuge", "caravan", "kingsmarch"} {
		if strings.Contains(id, token) || strings.Contains(name, token) {
			return true
		}
	}
	return false
}
