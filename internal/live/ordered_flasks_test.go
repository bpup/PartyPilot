package live

import (
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
)

func TestOrderedFlasksRouteEachLocalRoleWithMenuGuard(t *testing.T) {
	engine, err := NewWithBindings([]RoleBinding{{1, 1, 1, 101}, {2, 1, 2, 101}},
		1, follow.Config{StopDistance: 2, ResumeDistance: 3,
			CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	session, err := NewOrderedFlaskSession(engine, 750*time.Millisecond)
	if err != nil {
		t.Fatal(err)
	}
	start := time.Unix(100, 0).UTC()
	if !engine.ObserveClientArea(101, arealog.Signal{AreaID: "G2_4", Seed: 123,
		ObservedAt: start.Add(-time.Millisecond), Origin: "live"}) ||
		!engine.ObserveOrderedClient(OrderedClientFrame{ProcessID: 101,
			SeenUTC: start, ReceivedAt: start, TickMS: start.UnixMilli(), AreaHash: 55,
			CoordinateSpace: "world", PlayerOrderReliable: true,
			PlayerOrderSource: "verified fixture", Players: []OrderedLocalPlayer{
				{LocalPlayer: 1, X: 30 * worldPerGridUnit}, {LocalPlayer: 2, X: 0},
			}}) {
		t.Fatal("ordered flask test lacks a bound area and motion")
	}
	frame := func(at time.Time) OrderedVitalsFrame {
		return OrderedVitalsFrame{ProcessID: 101, SeenUTC: at, ReceivedAt: at,
			TickMS: at.UnixMilli(), AreaHash: 55, PlayerOrderReliable: true,
			PlayerOrderSource: "verified fixture", Players: []OrderedLocalVitals{
				{LocalPlayer: 1, Life: floatPtr(100), LifeMax: floatPtr(100),
					Mana: floatPtr(100), ManaMax: floatPtr(100)},
				{LocalPlayer: 2, Life: floatPtr(20), LifeMax: floatPtr(100),
					Mana: floatPtr(100), ManaMax: floatPtr(100)},
			}}
	}
	if !engine.ObserveOrderedVitals(frame(start)) {
		t.Fatal("initial slow frame rejected")
	}
	if got, err := session.ObserveOrderedVitals(frame(start), start); err != nil || len(got) != 0 {
		t.Fatalf("unknown menu state requested a flask: %+v %v", got, err)
	}
	if err := session.SetMenuGuard(2, "clear"); err != nil {
		t.Fatal(err)
	}
	active := frame(start.Add(time.Second))
	if !engine.ObserveOrderedVitals(active) {
		t.Fatal("active slow frame rejected")
	}
	got, err := session.ObserveOrderedVitals(active, active.SeenUTC)
	if err != nil || len(got) != 1 || got[0].Player != 2 || got[0].Resource != "life" {
		t.Fatalf("second local role did not receive its own life flask: %+v %v", got, err)
	}
	if replay, err := session.ObserveOrderedVitals(active, active.SeenUTC); err != nil || len(replay) != 0 {
		t.Fatal("replayed slow frame requested a second flask")
	}
	incomplete := frame(start.Add(2 * time.Second))
	incomplete.Players[0].Mana = nil
	engine.ObserveOrderedVitals(incomplete)
	if got, err := session.ObserveOrderedVitals(incomplete, incomplete.SeenUTC); err != nil || len(got) != 0 {
		t.Fatal("incomplete client resource frame produced a partial activation")
	}
	if got, err := session.ObserveOrderedVitals(frame(start.Add(3*time.Second)),
		start.Add(5*time.Second)); err != nil || len(got) != 0 {
		t.Fatal("stale client resource frame requested a flask")
	}
}

func TestOrderedFlasksCoverThirdClientRoles(t *testing.T) {
	bindings := []RoleBinding{{1, 1, 1, 101}, {2, 1, 2, 101},
		{3, 2, 1, 202}, {4, 2, 2, 202}, {5, 3, 1, 303}, {6, 3, 2, 303}}
	engine, err := NewWithBindings(bindings, 1, follow.Config{StopDistance: 2,
		ResumeDistance: 3, CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	session, err := NewOrderedFlaskSession(engine, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	if err := session.SetMenuGuard(6, "clear"); err != nil {
		t.Fatal(err)
	}
	now := time.Now().UTC()
	if !engine.ObserveClientArea(303, arealog.Signal{AreaID: "G2_4", Seed: 123,
		ObservedAt: now.Add(-time.Millisecond), Origin: "live"}) ||
		!engine.ObserveOrderedClient(OrderedClientFrame{ProcessID: 303,
			SeenUTC: now, ReceivedAt: now, TickMS: now.UnixMilli(), AreaHash: 55,
			CoordinateSpace: "world", PlayerOrderReliable: true,
			PlayerOrderSource: "verified fixture", Players: []OrderedLocalPlayer{
				{LocalPlayer: 1, X: 30 * worldPerGridUnit}, {LocalPlayer: 2, X: 0},
			}}) {
		t.Fatal("third client lacks area or ordered motion")
	}
	frame := OrderedVitalsFrame{ProcessID: 303, SeenUTC: now, ReceivedAt: now,
		TickMS: now.UnixMilli(), AreaHash: 55, PlayerOrderReliable: true,
		PlayerOrderSource: "verified fixture", Players: []OrderedLocalVitals{
			{LocalPlayer: 1, Life: floatPtr(100), LifeMax: floatPtr(100),
				Mana: floatPtr(100), ManaMax: floatPtr(100)},
			{LocalPlayer: 2, Life: floatPtr(15), LifeMax: floatPtr(100),
				Mana: floatPtr(100), ManaMax: floatPtr(100)},
		}}
	if !engine.ObserveOrderedVitals(frame) {
		t.Fatal("third client's slow frame rejected")
	}
	got, err := session.ObserveOrderedVitals(frame, now)
	if err != nil || len(got) != 1 || got[0].Player != 6 {
		t.Fatalf("third client's local P2 did not map to party P6: %+v %v", got, err)
	}
}

func TestOrderedFlaskHoldsWithoutMatchingAreaAndMotion(t *testing.T) {
	engine, err := NewWithBindings([]RoleBinding{{1, 1, 1, 101}, {2, 1, 2, 101}},
		1, follow.Config{StopDistance: 2, ResumeDistance: 3,
			CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	session, err := NewOrderedFlaskSession(engine, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	if err := session.SetMenuGuard(2, "clear"); err != nil {
		t.Fatal(err)
	}
	now := time.Now().UTC()
	vitals := OrderedVitalsFrame{ProcessID: 101, SeenUTC: now, ReceivedAt: now,
		TickMS: now.UnixMilli(), AreaHash: 55, PlayerOrderReliable: true,
		PlayerOrderSource: "verified fixture", Players: []OrderedLocalVitals{
			{LocalPlayer: 1, Life: floatPtr(100), LifeMax: floatPtr(100),
				Mana: floatPtr(100), ManaMax: floatPtr(100)},
			{LocalPlayer: 2, Life: floatPtr(15), LifeMax: floatPtr(100),
				Mana: floatPtr(100), ManaMax: floatPtr(100)},
		}}
	if !engine.ObserveOrderedVitals(vitals) {
		t.Fatal("slow frame rejected")
	}
	if got, err := session.ObserveOrderedVitals(vitals, now); err != nil || len(got) != 0 {
		t.Fatal("orphaned resource frame requested a flask")
	}
	if !engine.ObserveClientArea(101, arealog.Signal{AreaID: "G2_4", Seed: 123,
		ObservedAt: now.Add(-time.Millisecond), Origin: "live"}) ||
		!engine.ObserveOrderedClient(OrderedClientFrame{ProcessID: 101,
			SeenUTC: now, ReceivedAt: now, TickMS: now.UnixMilli(), AreaHash: 56,
			CoordinateSpace: "world", PlayerOrderReliable: true,
			PlayerOrderSource: "verified fixture", Players: []OrderedLocalPlayer{
				{LocalPlayer: 1, X: 30 * worldPerGridUnit}, {LocalPlayer: 2, X: 0},
			}}) {
		t.Fatal("area and mismatched motion rejected")
	}
	if got, err := session.ObserveOrderedVitals(vitals, now); err != nil || len(got) != 0 {
		t.Fatal("different area hashes requested a flask")
	}
}
