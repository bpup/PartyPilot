package focus

import "testing"

func TestFocusPausesSelectedFollowerAndReturns(t *testing.T) {
	router, err := New([]Player{{Slot: 1, Client: 1}, {Slot: 2, Client: 2}, {Slot: 3, Client: 2}}, 1)
	if err != nil {
		t.Fatal(err)
	}
	if !router.AutomationAllowed(2) || !router.AutomationAllowed(3) {
		t.Fatal("followers should be eligible")
	}
	if err := router.Focus(2); err != nil {
		t.Fatal(err)
	}
	if router.PhysicalInputTarget().Slot != 2 {
		t.Fatal("input target did not switch")
	}
	if router.AutomationAllowed(2) || !router.AutomationAllowed(3) {
		t.Fatal("wrong automation routing")
	}
	router.ReturnToLeader()
	if router.Focused().Slot != 1 || !router.AutomationAllowed(2) {
		t.Fatal("focus did not return")
	}
}

func TestRejectInvalidRosterAndUnknownFocus(t *testing.T) {
	cases := [][]Player{
		nil,
		{{Slot: 1, Client: 1}, {Slot: 1, Client: 2}},
		{{Slot: 1, Client: 1}, {Slot: 2, Client: 1}, {Slot: 3, Client: 1}},
		{{Slot: 7, Client: 1}},
	}
	for _, players := range cases {
		if _, err := New(players, 1); err == nil {
			t.Fatalf("accepted invalid roster: %+v", players)
		}
	}
	router, err := New([]Player{{Slot: 1, Client: 1}, {Slot: 2, Client: 2}}, 1)
	if err != nil {
		t.Fatal(err)
	}
	if err := router.Focus(3); err == nil || router.Focused().Slot != 1 {
		t.Fatal("unknown focus changed route")
	}
	if router.AutomationAllowed(3) {
		t.Fatal("unknown player was enabled")
	}
}
