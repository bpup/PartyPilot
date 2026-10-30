package clientlaunch

import "testing"

func TestValidatePreResumeClients(t *testing.T) {
	paths := []string{"PathOfExile.exe", "PathOfExile_x64.exe", "PathOfExile.exe"}
	ready := func(int) error { return nil }
	for _, selected := range []map[int]func(int) error{
		{1: ready}, {1: ready, 2: ready}, {1: ready, 2: ready, 3: ready}, {3: ready},
	} {
		if err := validatePreResumeClients(paths, nil, selected); err != nil {
			t.Fatalf("valid per-client pre-resume selection failed: %v", err)
		}
	}
	for _, selected := range []map[int]func(int) error{
		{0: ready}, {4: ready}, {2: nil},
	} {
		if err := validatePreResumeClients(paths, nil, selected); err == nil {
			t.Fatalf("accepted invalid per-client pre-resume selection: %v", selected)
		}
	}
	if err := validatePreResumeClients(paths, map[int]string{2: "steam://rungameid/2694490"},
		map[int]func(int) error{2: ready}); err == nil {
		t.Fatal("accepted a store URI for a suspended standalone launch")
	}
	if err := validatePreResumeClients([]string{"PathOfExileSteam.exe"}, nil,
		map[int]func(int) error{1: ready}); err == nil {
		t.Fatal("accepted a store game executable for pre-resume input")
	}
}
