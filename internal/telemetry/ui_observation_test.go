package telemetry

import (
	"strings"
	"testing"
)

func TestUiObservationPreservesUnknownAndRejectsInvalidStatus(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"UiObservationSeenUtc":"2026-10-26T00:00:00Z","UiObservationStatus":"observed",` +
		`"UiObservation":{"LoadingObserved":false,"InventoryPanelVisible":true,"ChatBoxVisible":null}}`
	sample, err := Decode([]byte(base))
	if err != nil || sample.UiObservation == nil || sample.UiObservation.LoadingObserved == nil ||
		*sample.UiObservation.LoadingObserved || sample.UiObservation.InventoryPanelVisible == nil ||
		!*sample.UiObservation.InventoryPanelVisible || sample.UiObservation.ChatBoxVisible != nil {
		t.Fatalf("UI observation was not preserved: %+v, %v", sample.UiObservation, err)
	}
	for _, changed := range []string{
		strings.Replace(base, `"UiObservationStatus":"observed"`, `"UiObservationStatus":"scan_failed"`, 1),
		strings.Replace(base, `"UiObservationStatus":"observed"`, `"UiObservationStatus":"safe"`, 1),
		strings.Replace(base, `"UiObservationSeenUtc":"2026-10-26T00:00:00Z"`, `"UiObservationSeenUtc":null`, 1),
		strings.Replace(base, `"UiObservation":{"LoadingObserved":false,"InventoryPanelVisible":true,"ChatBoxVisible":null}`, `"UiObservation":null`, 1),
	} {
		if _, err := Decode([]byte(changed)); err == nil {
			t.Fatalf("invalid UI observation accepted: %s", changed)
		}
	}
	failed := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"UiObservationSeenUtc":"2026-10-26T00:00:00Z","UiObservationStatus":"scan_failed"}`
	if _, err := Decode([]byte(failed)); err != nil {
		t.Fatalf("scan failure should remain explicit: %v", err)
	}
}
