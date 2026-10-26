package telemetry

import "testing"

func TestRawTerrainSummaryRemainsDiagnostic(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3`
	valid := base + `,"TerrainStatus":"raw_values_observed","TerrainSeenUtc":"2026-10-26T00:00:00Z",` +
		`"TerrainSummary":{"Rows":2,"MinColumns":1,"MaxColumns":3,"SampledCells":4,` +
		`"Values":[{"Value":0,"Count":1},{"Value":1,"Count":2},{"Value":2,"Count":1}]}}`
	sample, err := Decode([]byte(valid))
	if err != nil || sample.TerrainSummary == nil || sample.TerrainSummary.SampledCells != 4 {
		t.Fatalf("raw terrain diagnostic lost: %+v, %v", sample.TerrainSummary, err)
	}
	for _, suffix := range []string{
		`,"TerrainStatus":"walkable","TerrainSeenUtc":"2026-10-26T00:00:00Z"}`,
		`,"TerrainStatus":"raw_values_observed","TerrainSeenUtc":"2026-10-26T00:00:00Z",` +
			`"TerrainSummary":{"Rows":1,"MinColumns":1,"MaxColumns":1,"SampledCells":1,` +
			`"Values":[{"Value":1,"Count":2}]}}`,
		`,"TerrainStatus":"scan_failed","TerrainSeenUtc":"2026-10-26T00:00:00Z",` +
			`"TerrainSummary":{"Rows":1,"MinColumns":1,"MaxColumns":1,"SampledCells":1}}`,
	} {
		if _, err := Decode([]byte(base + suffix)); err == nil {
			t.Fatalf("invalid terrain evidence accepted: %s", suffix)
		}
	}
}
