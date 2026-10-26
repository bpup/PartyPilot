package telemetry

import "testing"

func TestCoordinateSpaceMustBeExplicitWhenProvided(t *testing.T) {
	base := `{"ProcessId":101,"SeenUtc":"2026-10-27T10:00:00Z","X":108.69565217391305,"Y":0,"Z":0,`
	world, err := Decode([]byte(base + `"CoordinateSpace":"world"}`))
	if err != nil || world.CoordinateSpace != "world" {
		t.Fatalf("world position marker was not preserved: %+v %v", world, err)
	}
	if _, err := Decode([]byte(base + `"CoordinateSpace":"grid"}`)); err == nil {
		t.Fatal("unknown coordinate space was accepted")
	}
	legacy, err := Decode([]byte(base[:len(base)-1] + `}`))
	if err != nil || legacy.CoordinateSpace != "" {
		t.Fatalf("read-only legacy diagnostic could not be decoded: %+v %v", legacy, err)
	}
}
