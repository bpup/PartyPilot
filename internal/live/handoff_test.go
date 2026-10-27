package live

import (
	"reflect"
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
	"partypilot/internal/telemetry"
)

func TestCrossAreaRequestsRequireFreshConfirmedClients(t *testing.T) {
	now := time.Now().UTC().Truncate(time.Millisecond)
	engine, err := New(map[int]int{1: 101, 2: 202, 3: 303}, 1,
		follow.Config{StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10,
			MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	for slot, pid := range map[int]int{1: 101, 2: 202, 3: 303} {
		if !engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "A", Seed: 1,
			ObservedAt: now, Origin: "live"}) {
			t.Fatal("initial area was rejected")
		}
		if !observeGridSample(engine, telemetry.Sample{ProcessID: pid,
			SeenUTC: now.Add(time.Millisecond), AreaHash: uint32(pid), X: float64(slot)}) {
			t.Fatal("initial position was rejected")
		}
	}
	if got := engine.CrossAreaRequests(now.Add(time.Millisecond)); len(got) != 0 {
		t.Fatalf("same instance requested warp: %+v", got)
	}
	if !engine.ObserveArea(arealog.Signal{Slot: 1, AreaID: "B", Seed: 2,
		ObservedAt: now.Add(2 * time.Millisecond), Origin: "live"}) {
		t.Fatal("leader transition was rejected")
	}
	if got := engine.CrossAreaRequests(now.Add(2 * time.Millisecond)); len(got) != 0 {
		t.Fatalf("old leader position requested warp: %+v", got)
	}
	if !observeGridSample(engine, telemetry.Sample{ProcessID: 101,
		SeenUTC: now.Add(3 * time.Millisecond), AreaHash: 404, X: 10}) {
		t.Fatal("new leader position was rejected")
	}
	want := []HandoffRequest{
		{DestinationClient: 2, DriverPlayer: 2, TargetPlayer: 1, State: "warp_requested"},
		{DestinationClient: 3, DriverPlayer: 3, TargetPlayer: 1, State: "warp_requested"},
	}
	if got := engine.CrossAreaRequests(now.Add(3 * time.Millisecond)); !reflect.DeepEqual(got, want) {
		t.Fatalf("wrong cross-area requests: %+v", got)
	}
	engine.SetFollowPaused(true)
	if got := engine.CrossAreaRequests(now.Add(4 * time.Millisecond)); len(got) != 0 {
		t.Fatalf("paused follow requested warp: %+v", got)
	}
	engine.SetFollowPaused(false)
	if got := engine.CrossAreaRequests(now.Add(2 * time.Second)); len(got) != 0 {
		t.Fatalf("stale positions requested warp: %+v", got)
	}
}

func TestCrossAreaRequestUsesFirstLocalPlayerAsDriver(t *testing.T) {
	now := time.Now().UTC().Truncate(time.Millisecond)
	engine, err := NewWithBindings([]RoleBinding{
		{Slot: 1, Client: 1, LocalPlayer: 1, ProcessID: 101},
		{Slot: 2, Client: 1, LocalPlayer: 2, ProcessID: 101},
		{Slot: 3, Client: 2, LocalPlayer: 1, ProcessID: 202},
		{Slot: 4, Client: 2, LocalPlayer: 2, ProcessID: 202},
	}, 1, follow.Config{StopDistance: 2, ResumeDistance: 3,
		CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	for pid, seed := range map[int]int64{101: 2, 202: 1} {
		if !engine.ObserveClientArea(pid, arealog.Signal{AreaID: "A", Seed: seed,
			ObservedAt: now, Origin: "live"}) {
			t.Fatal("client area was rejected")
		}
		frame := OrderedClientFrame{ProcessID: pid,
			SeenUTC: now.Add(time.Millisecond), ReceivedAt: now.Add(time.Millisecond),
			TickMS: now.UnixMilli() + 1, AreaHash: uint32(pid),
			AreaInstanceAddress: "0x100000", VectorHeaderAddress: "0x100100",
			OrderedAddresses: []string{"0x200000", "0x300000"},
			CoordinateSpace:  "world", PlayerOrderReliable: true,
			PlayerOrderSource: "experimental_static_vector_0.6.17",
			Players:           []OrderedLocalPlayer{{LocalPlayer: 1, X: 100}, {LocalPlayer: 2, X: 90}}}
		if !engine.ObserveOrderedClient(frame) {
			t.Fatal("ordered client frame was rejected")
		}
	}
	want := []HandoffRequest{{DestinationClient: 2, DriverPlayer: 3,
		TargetPlayer: 1, State: "warp_requested"}}
	if got := engine.CrossAreaRequests(now.Add(time.Millisecond)); !reflect.DeepEqual(got, want) {
		t.Fatalf("dual-role client requested the wrong driver: %+v", got)
	}
	positions := engine.CurrentPositions(now.Add(time.Millisecond))
	if len(positions) != 4 || positions[2].Slot != 3 || positions[2].ProcessID != 202 ||
		positions[2].AreaHash != 202 || positions[2].X != 100 ||
		positions[3].Slot != 4 || positions[3].ProcessID != 202 || positions[3].X != 90 {
		t.Fatalf("dual-role positions were not exported as distinct current witnesses: %+v", positions)
	}
	if got := engine.CurrentPositions(now.Add(2 * time.Second)); len(got) != 0 {
		t.Fatalf("stale dual-role positions were exported: %+v", got)
	}
	engine.SetFollowPaused(true)
	if got := engine.CurrentPositions(now.Add(time.Millisecond)); len(got) != 0 {
		t.Fatalf("paused dual-role positions were exported: %+v", got)
	}
	engine.SetFollowPaused(false)
	if err := engine.SetFollowTarget(4, 2); err != nil {
		t.Fatal(err)
	}
	if got := engine.CrossAreaRequests(now.Add(time.Millisecond)); len(got) != 0 {
		t.Fatalf("conflicting targets on one client requested an ambiguous warp: %+v", got)
	}
}
