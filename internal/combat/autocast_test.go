package combat

import (
	"reflect"
	"testing"
	"time"
)

func TestAutoCastShippingRowsAndValidation(t *testing.T) {
	rows := DefaultAutoCastRows()
	if len(rows) != 3 || rows[0].Enabled || rows[1].Enabled || rows[2].Enabled ||
		rows[0].CooldownMS != 8250 || rows[1].Target != "P2" ||
		rows[2].CooldownMS != 5000 {
		t.Fatalf("auto-cast shipping rows changed: %+v", rows)
	}
	if _, err := NewAutoCastEngine(rows); err != nil {
		t.Fatal(err)
	}
	for _, changed := range []func(*AutoCastRow){
		func(row *AutoCastRow) { row.Input = "LT" },
		func(row *AutoCastRow) { row.Target = "P7" },
		func(row *AutoCastRow) { row.Trigger = "On hit" },
		func(row *AutoCastRow) { row.CooldownMS = 249 },
		func(row *AutoCastRow) { row.ID = "" },
	} {
		copyRows := append([]AutoCastRow(nil), rows...)
		changed(&copyRows[0])
		if _, err := NewAutoCastEngine(copyRows); err == nil {
			t.Fatalf("invalid auto-cast row accepted: %+v", copyRows[0])
		}
	}
}

func TestAutoCastTimerPulseAndPairRouting(t *testing.T) {
	engine, err := NewAutoCastEngine([]AutoCastRow{{ID: "a", Input: "D-pad Up",
		Target: "Both", Trigger: "Timer", CooldownMS: 1000, Enabled: true}})
	if err != nil {
		t.Fatal(err)
	}
	start := time.Unix(100, 0)
	tick := func(at time.Time, enabled, combatRecent bool) []AutoCastPulse {
		t.Helper()
		got, err := engine.Step(at, enabled, true, combatRecent, false, false,
			[]int{1, 2, 3}, map[int]bool{2: true}, func() int { return 50 })
		if err != nil {
			t.Fatal(err)
		}
		return got
	}
	if got := tick(start, false, true); len(got) != 0 {
		t.Fatalf("disabled cast emitted pulses: %+v", got)
	}
	if got := tick(start, true, false); len(got) != 0 {
		t.Fatalf("combat gate emitted pulses: %+v", got)
	}
	if got := tick(start, true, true); len(got) != 0 {
		t.Fatalf("first observation should initialize cooldown: %+v", got)
	}
	if got := tick(start.Add(time.Second), true, true); len(got) != 0 {
		t.Fatalf("jitter start was ignored: %+v", got)
	}
	for _, elapsed := range []time.Duration{1050 * time.Millisecond, 1169 * time.Millisecond} {
		if got := tick(start.Add(elapsed), true, true); !reflect.DeepEqual(got,
			[]AutoCastPulse{{Player: 1, Input: "D-pad Up"}}) {
			t.Fatalf("held pulse or target routing changed at %s: %+v", elapsed, got)
		}
	}
	if got := tick(start.Add(1170*time.Millisecond), true, true); len(got) != 0 {
		t.Fatalf("pulse lasted beyond 120 ms: %+v", got)
	}
	if got := tick(start.Add(2*time.Second), true, true); len(got) != 0 {
		t.Fatalf("cooldown restarted from trigger instead of scheduled start: %+v", got)
	}
	if got := tick(start.Add(2100*time.Millisecond), true, true); len(got) != 0 {
		t.Fatalf("second pulse ignored its jitter: %+v", got)
	}
	if got := tick(start.Add(2150*time.Millisecond), true, true); len(got) != 1 {
		t.Fatalf("timer did not repeat after cooldown: %+v", got)
	}
}

func TestAutoCastAfterAttackRequiresEdgeAndCooldown(t *testing.T) {
	engine, err := NewAutoCastEngine([]AutoCastRow{{ID: "after", Input: "Y",
		Target: "Client 2 Pair", Trigger: "After attack", CooldownMS: 500, Enabled: true}})
	if err != nil {
		t.Fatal(err)
	}
	start := time.Unix(100, 0)
	tick := func(at time.Time, active, edge bool) []AutoCastPulse {
		t.Helper()
		got, err := engine.Step(at, true, false, false, active, edge,
			[]int{1, 3, 4}, nil, func() int { return 1 })
		if err != nil {
			t.Fatal(err)
		}
		return got
	}
	if got := tick(start, true, true); len(got) != 0 {
		t.Fatalf("attack edge bypassed first cooldown: %+v", got)
	}
	if got := tick(start.Add(500*time.Millisecond), true, false); len(got) != 0 {
		t.Fatalf("held attack triggered after-attack row: %+v", got)
	}
	if got := tick(start.Add(500*time.Millisecond), true, true); len(got) != 0 {
		t.Fatalf("after-attack pulse ignored jitter: %+v", got)
	}
	if got := tick(start.Add(501*time.Millisecond), true, false); !reflect.DeepEqual(got,
		[]AutoCastPulse{{Player: 3, Input: "Y"}, {Player: 4, Input: "Y"}}) {
		t.Fatalf("after-attack target routing changed: %+v", got)
	}
	if got := tick(start.Add(1001*time.Millisecond), true, false); len(got) != 0 {
		t.Fatalf("after-attack repeated without another edge: %+v", got)
	}
}
