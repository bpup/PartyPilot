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

func TestPackedTerrainPatchIsDiagnosticAndBounded(t *testing.T) {
	base := `{"ProcessId":123,"SeenUtc":"2026-10-26T00:00:00Z","X":1,"Y":2,"Z":3,` +
		`"TerrainStatus":"unavailable","TerrainSeenUtc":"2026-10-26T00:00:00Z",`
	valid := base + `"PackedTerrainStatus":"candidate_patch_observed",` +
		`"PackedTerrainProbe":{"AreaAddress":"0x100000","Anchor":"state_pointer_static_656",` +
		`"Width":92,"Height":92,"BytesPerRow":46,"PatchX":6,"PatchY":16,` +
		`"PatchRows":["111","101","111"],"SampledNibbles":8192,"InRangeNibbles":8192}}`
	sample, err := Decode([]byte(valid))
	if err != nil || sample.PackedTerrainProbe == nil || sample.PackedTerrainProbe.PatchRows[1] != "101" {
		t.Fatalf("valid read-only patch lost: %+v, %v", sample.PackedTerrainProbe, err)
	}
	for _, suffix := range []string{
		`"PackedTerrainStatus":"candidate_patch_observed"}`,
		`"PackedTerrainStatus":"no_candidate","PackedTerrainProbe":{"Width":92}}`,
		`"PackedTerrainStatus":"candidate_patch_observed","PackedTerrainProbe":` +
			`{"AreaAddress":"0x100000","Anchor":"ingame_data_address",` +
			`"Width":92,"Height":92,"BytesPerRow":46,"PatchX":90,"PatchY":16,` +
			`"PatchRows":["111"],"SampledNibbles":8192,"InRangeNibbles":8192}}`,
		`"PackedTerrainStatus":"candidate_patch_observed","PackedTerrainProbe":` +
			`{"AreaAddress":"0x100000","Anchor":"ingame_data_address",` +
			`"Width":92,"Height":92,"BytesPerRow":46,"PatchX":6,"PatchY":16,` +
			`"PatchRows":["1?1"],"SampledNibbles":8192,"InRangeNibbles":8192}}`,
	} {
		if _, err := Decode([]byte(base + suffix)); err == nil {
			t.Fatalf("invalid terrain patch accepted: %s", suffix)
		}
	}
}
