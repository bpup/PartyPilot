package live

import (
	"encoding/json"
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
)

func TestSeparateOrderedVitalsDoNotReplaceMotion(t *testing.T) {
	now := time.Now().UTC()
	engine, err := NewWithBindings([]RoleBinding{{1, 1, 1, 101}, {2, 1, 2, 101}},
		1, follow.Config{StopDistance: 2, ResumeDistance: 3,
			CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	if !engine.ObserveClientArea(101, arealog.Signal{AreaID: "G2_4", Seed: 123,
		ObservedAt: now, Origin: "live"}) {
		t.Fatal("client area rejected")
	}
	motion := OrderedClientFrame{ProcessID: 101, SeenUTC: now.Add(time.Millisecond),
		ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
		AreaHash: 55, CoordinateSpace: "world", PlayerOrderReliable: true,
		PlayerOrderSource: "verified fixture", Players: []OrderedLocalPlayer{
			{LocalPlayer: 1, X: 30 * worldPerGridUnit}, {LocalPlayer: 2, X: 0},
		}}
	if !engine.ObserveOrderedClient(motion) ||
		engine.Step(motion.SeenUTC)[2].State != follow.CatchingUp {
		t.Fatal("position-only motion did not follow")
	}
	vitals := OrderedVitalsFrame{ProcessID: 101, SeenUTC: now.Add(2 * time.Millisecond),
		ReceivedAt: now.Add(2 * time.Millisecond), TickMS: now.UnixMilli() + 2,
		AreaHash: 55, PlayerOrderReliable: true,
		PlayerOrderSource: "verified fixture", Players: []OrderedLocalVitals{
			{LocalPlayer: 1, Life: floatPtr(100), LifeMax: floatPtr(100)},
			{LocalPlayer: 2, Life: floatPtr(90), LifeMax: floatPtr(100)},
		}}
	if !engine.ObserveOrderedVitals(vitals) ||
		!engine.Step(vitals.SeenUTC)[2].ManualOverrideEligible {
		t.Fatal("separate slow vitals did not enrich fresh motion")
	}
	motion.SeenUTC = now.Add(3 * time.Millisecond)
	motion.ReceivedAt = motion.SeenUTC
	motion.TickMS += 2
	if !engine.ObserveOrderedClient(motion) ||
		!engine.Step(motion.SeenUTC)[2].ManualOverrideEligible {
		t.Fatal("later fast motion lost still-fresh slow vitals")
	}
	dead := vitals
	dead.SeenUTC = now.Add(4 * time.Millisecond)
	dead.ReceivedAt = dead.SeenUTC
	dead.TickMS += 2
	dead.Players = append([]OrderedLocalVitals(nil), vitals.Players...)
	dead.Players[1].Life = floatPtr(0)
	if !engine.ObserveOrderedVitals(dead) {
		t.Fatal("new slow death frame rejected")
	}
	if command := engine.Step(dead.SeenUTC)[2]; command.State != follow.Waiting ||
		command.Reason != "dead follower" {
		t.Fatalf("slow death did not stop the follower: %+v", command)
	}
	if engine.ObserveOrderedVitals(vitals) ||
		engine.Step(dead.SeenUTC)[2].State != follow.Waiting {
		t.Fatal("older living vitals replaced a newer death observation")
	}
	broken := dead
	broken.SeenUTC = now.Add(5 * time.Millisecond)
	broken.ReceivedAt = broken.SeenUTC
	broken.TickMS++
	broken.Players = broken.Players[:1]
	if engine.ObserveOrderedVitals(broken) || len(engine.samples) != 2 {
		t.Fatal("incomplete slow vitals changed motion positions")
	}
	if command := engine.Step(broken.SeenUTC)[2]; command.State != follow.CatchingUp ||
		command.ManualOverrideEligible {
		t.Fatalf("old vitals survived an incomplete current slow frame: %+v", command)
	}
	vitals.SeenUTC = now.Add(6 * time.Millisecond)
	vitals.ReceivedAt = vitals.SeenUTC
	vitals.TickMS += 4
	if !engine.ObserveOrderedVitals(vitals) {
		t.Fatal("slow vitals did not recover")
	}
	motion.SeenUTC = now.Add(7 * time.Millisecond)
	motion.ReceivedAt = motion.SeenUTC
	motion.TickMS += 4
	motion.AreaHash = 56
	if !engine.ObserveOrderedClient(motion) ||
		engine.Step(motion.SeenUTC)[2].ManualOverrideEligible {
		t.Fatal("vitals crossed an area hash change")
	}
}

func TestOrderedVitalsWireRejectsCandidateAndTrailingData(t *testing.T) {
	now := time.Now().UTC()
	wire := struct {
		Kind string `json:"Kind"`
		OrderedVitalsFrame
	}{Kind: "ordered_vitals", OrderedVitalsFrame: OrderedVitalsFrame{
		ProcessID: 101, SeenUTC: now, TickMS: now.UnixMilli(),
		PlayerOrderReliable: true, PlayerOrderSource: "verified fixture",
		Players: []OrderedLocalVitals{{LocalPlayer: 1, Life: floatPtr(100), LifeMax: floatPtr(100)}},
	}}
	data, err := json.Marshal(wire)
	if err != nil {
		t.Fatal(err)
	}
	if frame, err := DecodeOrderedVitals(data, now); err != nil ||
		frame.ProcessID != 101 || len(frame.Players) != 1 || !frame.ReceivedAt.Equal(now) {
		t.Fatalf("slow ordered packet rejected: %+v %v", frame, err)
	}
	for _, invalid := range [][]byte{
		[]byte(`{"ProcessId":101,"LocalSlotCandidates":{"Status":"two_slot_candidate"}}`),
		append(append([]byte{}, data...), []byte(` {}`)...),
		append(append([]byte{}, data[:len(data)-1]...), []byte(`,"Unknown":true}`)...),
	} {
		if _, err := DecodeOrderedVitals(invalid, now); err == nil {
			t.Fatalf("unverified packet accepted: %s", invalid)
		}
	}
}

func TestSeparateVitalsNeedNonzeroMatchingMotionHashForManualInput(t *testing.T) {
	now := time.Now().UTC()
	engine, err := NewWithBindings([]RoleBinding{{1, 1, 1, 101}, {2, 1, 2, 101}},
		1, follow.Config{StopDistance: 2, ResumeDistance: 3,
			CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	engine.ObserveClientArea(101, arealog.Signal{AreaID: "G2_4", Seed: 123,
		ObservedAt: now, Origin: "live"})
	motion := OrderedClientFrame{ProcessID: 101, SeenUTC: now.Add(time.Millisecond),
		ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
		CoordinateSpace: "world", PlayerOrderReliable: true,
		PlayerOrderSource: "verified fixture", Players: []OrderedLocalPlayer{
			{LocalPlayer: 1, X: 30 * worldPerGridUnit}, {LocalPlayer: 2, X: 0},
		}}
	if !engine.ObserveOrderedClient(motion) {
		t.Fatal("motion rejected")
	}
	vitals := OrderedVitalsFrame{ProcessID: 101, SeenUTC: now.Add(2 * time.Millisecond),
		ReceivedAt: now.Add(2 * time.Millisecond), TickMS: now.UnixMilli() + 2,
		AreaHash: 55, PlayerOrderReliable: true,
		PlayerOrderSource: "verified fixture", Players: []OrderedLocalVitals{
			{LocalPlayer: 1, Life: floatPtr(100), LifeMax: floatPtr(100)},
			{LocalPlayer: 2, Life: floatPtr(90), LifeMax: floatPtr(100)},
		}}
	if !engine.ObserveOrderedVitals(vitals) ||
		engine.Step(vitals.SeenUTC)[2].ManualOverrideEligible {
		t.Fatal("unmatched slow area hash authorized manual input")
	}
	motion.AreaHash = 55
	motion.SeenUTC = now.Add(3 * time.Millisecond)
	motion.ReceivedAt = motion.SeenUTC
	motion.TickMS += 2
	if !engine.ObserveOrderedClient(motion) ||
		!engine.Step(motion.SeenUTC)[2].ManualOverrideEligible {
		t.Fatal("matched slow resource did not restore manual eligibility")
	}
}
