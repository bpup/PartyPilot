package telemetry

import (
	"strings"
	"testing"
)

func TestAreaSafetyObservationRequiresMatchingHashAndCompleteFlags(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","AreaHash":42,"X":1,"Y":2,"Z":3,` +
		`"AreaSafetySeenUtc":"2026-10-26T00:00:00Z","AreaSafetyStatus":"observed",` +
		`"AreaSafety":{"Hash":42,"Town":false,"Hideout":false,"Peaceful":true}}`
	sample, err := Decode([]byte(base))
	if err != nil || sample.AreaSafety == nil || sample.AreaSafety.Peaceful == nil ||
		!*sample.AreaSafety.Peaceful {
		t.Fatalf("matching area flags were not preserved: %+v, %v", sample.AreaSafety, err)
	}
	for _, changed := range []string{
		strings.Replace(base, `"AreaSafety":{"Hash":42`, `"AreaSafety":{"Hash":43`, 1),
		strings.Replace(base, `"Peaceful":true`, `"Peaceful":null`, 1),
		strings.Replace(base, `"AreaSafetyStatus":"observed"`, `"AreaSafetyStatus":"safe"`, 1),
		strings.Replace(base, `"AreaSafetySeenUtc":"2026-10-26T00:00:00Z"`, `"AreaSafetySeenUtc":null`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("unsupported area safety assertion accepted: %s", changed)
		}
	}
	mismatch := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","AreaHash":42,"X":1,"Y":2,"Z":3,` +
		`"AreaSafetySeenUtc":"2026-10-26T00:00:00Z","AreaSafetyStatus":"area_mismatch"}`
	if _, err := Decode([]byte(mismatch)); err != nil {
		t.Fatalf("explicit area mismatch should remain diagnostic: %v", err)
	}
}
