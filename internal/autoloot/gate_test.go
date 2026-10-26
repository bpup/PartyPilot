package autoloot

import (
	"testing"
	"time"
)

func TestGateRequiresExactCardAndAllSafetyEvidence(t *testing.T) {
	gate := NewGate()
	now := time.Unix(1000, 0)
	ready := Conditions{CatalogApproved: true, CapacityAllows: true, InventoryLimitAllows: true,
		MenuClear: true, OwnInventoryClosed: true, InventoryAutomationFree: true, Alive: true}
	card := &Card{SelectedItem: "0xABC", BaseID: "CurrencyExalted", Player: 1,
		Targetable: true, ReceivedAt: now}
	input := Frame{Now: now, Player: 1, FrameSequence: 0, Card: card, Conditions: ready}
	check := func(want bool) {
		t.Helper()
		got, err := gate.Step(input)
		if err != nil || got.Triggered != want || got.PulseActive != want {
			t.Fatalf("pickup triggered=%v, want %v: %+v, %v", got.Triggered, want, got, err)
		}
		_ = gate.Reset(1)
	}
	input.Card = nil
	check(false)
	input.Card = card
	input.Card.Player = 2
	check(false)
	input.Card.Player = 1
	input.Card.ReceivedAt = now.Add(-351 * time.Millisecond)
	check(false)
	input.Card.ReceivedAt = now
	input.Card.Targetable = false
	check(false)
	input.Card.Targetable = true
	for _, condition := range []func(*Conditions){
		func(c *Conditions) { c.CatalogApproved = false },
		func(c *Conditions) { c.CapacityAllows = false },
		func(c *Conditions) { c.InventoryLimitAllows = false },
		func(c *Conditions) { c.MenuClear = false },
		func(c *Conditions) { c.OwnInventoryClosed = false },
		func(c *Conditions) { c.InventoryAutomationFree = false },
		func(c *Conditions) { c.Alive = false },
	} {
		input.Conditions = ready
		condition(&input.Conditions)
		check(false)
	}
	input.Conditions = ready
	check(true)
}

func TestGatePulseRetryClearAndPlayerIsolation(t *testing.T) {
	gate := NewGate()
	start := time.Unix(1000, 0)
	ready := Conditions{CatalogApproved: true, CapacityAllows: true, InventoryLimitAllows: true,
		MenuClear: true, OwnInventoryClosed: true, InventoryAutomationFree: true, Alive: true}
	card := &Card{SelectedItem: "0xABC", BaseID: "CurrencyExalted", Player: 1,
		Targetable: true, ReceivedAt: start}
	input := Frame{Now: start, Player: 1, FrameSequence: 1, Card: card, Conditions: ready}
	step := func(wantPulse, wantTrigger bool, wantAttempt int) {
		t.Helper()
		got, err := gate.Step(input)
		if err != nil || got.PulseActive != wantPulse || got.Triggered != wantTrigger || got.Attempt != wantAttempt {
			t.Fatalf("unexpected pickup decision: %+v, %v", got, err)
		}
	}
	step(true, true, 1)
	input.Now = start.Add(50 * time.Millisecond)
	step(true, false, 1)
	input.Now = start.Add(80 * time.Millisecond)
	step(false, false, 1)
	input.FrameSequence = 2
	input.Card.ReceivedAt = input.Now
	step(false, false, 1)
	input.Now = start.Add(180 * time.Millisecond)
	input.FrameSequence = 3
	input.Card.ReceivedAt = input.Now
	step(true, true, 2)
	input.Now = start.Add(190 * time.Millisecond)
	input.FrameSequence = 4
	input.Card = nil
	step(false, false, 0)
	input.Now = start.Add(200 * time.Millisecond)
	input.FrameSequence = 5
	input.Card = card
	input.Card.ReceivedAt = input.Now
	step(true, true, 1)
	input.Player = 2
	input.Card.Player = 1
	input.Now = start.Add(201 * time.Millisecond)
	step(false, false, 0)
}

func TestGateStopsActivePulseWhenSafetyCloses(t *testing.T) {
	gate := NewGate()
	start := time.Unix(1000, 0)
	card := &Card{SelectedItem: "0xABC", BaseID: "CurrencyExalted", Player: 1,
		Targetable: true, ReceivedAt: start}
	ready := Conditions{CatalogApproved: true, CapacityAllows: true, InventoryLimitAllows: true,
		MenuClear: true, OwnInventoryClosed: true, InventoryAutomationFree: true, Alive: true}
	input := Frame{Now: start, Player: 1, FrameSequence: 1, Card: card, Conditions: ready}
	if got, _ := gate.Step(input); !got.PulseActive {
		t.Fatal("fixture did not start a pulse")
	}
	input.Now = start.Add(10 * time.Millisecond)
	input.Conditions.MenuClear = false
	if got, _ := gate.Step(input); got.PulseActive {
		t.Fatal("menu did not cancel pickup pulse")
	}
	input.Now = start.Add(20 * time.Millisecond)
	input.Conditions.MenuClear = true
	if got, _ := gate.Step(input); got.PulseActive {
		t.Fatal("old pickup pulse resumed after menu closed")
	}
	input.Now = start.Add(181 * time.Millisecond)
	input.Card.ReceivedAt = input.Now
	input.FrameSequence = 2
	if got, _ := gate.Step(input); !got.Triggered {
		t.Fatal("fresh card did not retry after safety recovered")
	}
}

func TestGateMapsEachClientPairToItsLocalPlayer(t *testing.T) {
	gate := NewGate()
	now := time.Unix(1000, 0)
	ready := Conditions{CatalogApproved: true, CapacityAllows: true, InventoryLimitAllows: true,
		MenuClear: true, OwnInventoryClosed: true, InventoryAutomationFree: true, Alive: true}
	for player := 1; player <= 6; player++ {
		localPlayer := (player-1)%2 + 1
		card := &Card{SelectedItem: "0xABC", BaseID: "CurrencyExalted", Player: localPlayer,
			Targetable: true, ReceivedAt: now}
		got, err := gate.Step(Frame{Now: now, Player: player, FrameSequence: 1, Card: card, Conditions: ready})
		if err != nil || !got.Triggered || got.Attempt != 1 {
			t.Fatalf("P%d's local P%d card rejected: %+v, %v", player, localPlayer, got, err)
		}
	}
}
