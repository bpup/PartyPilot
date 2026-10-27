package live

import (
	"encoding/json"
	"testing"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
	"partypilot/internal/partylog"
	"partypilot/internal/telemetry"
)

func TestOrderedWireIsSeparateFromCandidateSensorPacket(t *testing.T) {
	now := time.Now().UTC()
	encoded, err := json.Marshal(struct {
		Kind string `json:"Kind"`
		OrderedClientFrame
	}{Kind: "ordered_client", OrderedClientFrame: OrderedClientFrame{
		ProcessID: 101, SeenUTC: now, TickMS: now.UnixMilli(),
		CoordinateSpace: "world", PlayerOrderReliable: true,
		PlayerOrderSource: "verified test fixture",
		Players: []OrderedLocalPlayer{{LocalPlayer: 1, Life: floatPtr(100), LifeMax: floatPtr(100)},
			{LocalPlayer: 2, X: 10, Life: floatPtr(100), LifeMax: floatPtr(100)}},
	}})
	if err != nil {
		t.Fatal(err)
	}
	frame, err := DecodeOrderedClient(encoded, now.Add(time.Millisecond))
	if err != nil || frame.ProcessID != 101 || len(frame.Players) != 2 ||
		!frame.ReceivedAt.Equal(now.Add(time.Millisecond)) {
		t.Fatalf("ordered packet rejected: %+v %v", frame, err)
	}
	invalidCurrent := frame
	invalidCurrent.PlayerOrderReliable = false
	invalidBytes, err := json.Marshal(struct {
		Kind string `json:"Kind"`
		OrderedClientFrame
	}{Kind: "ordered_client", OrderedClientFrame: invalidCurrent})
	if err != nil {
		t.Fatal(err)
	}
	if decoded, err := DecodeOrderedClient(invalidBytes, now.Add(2*time.Millisecond)); err != nil ||
		decoded.PlayerOrderReliable {
		t.Fatalf("current loss-of-order frame must reach the engine to revoke old positions: %v", err)
	}
	for _, packet := range [][]byte{
		[]byte(`{"ProcessId":101,"SeenUtc":"2026-10-27T00:00:00Z","TickMs":1,"VisiblePlayers":[]}`),
		[]byte(`{"Kind":"ordered_client","ProcessId":101,"PlayerOrderReliable":false}`),
		append(append([]byte{}, encoded...), []byte(` {}`)...),
		append(append([]byte{}, encoded[:len(encoded)-1]...), []byte(`,"LocalSlotCandidates":{}}`)...),
	} {
		if _, err := DecodeOrderedClient(packet, now); err == nil {
			t.Fatalf("unverified or malformed packet accepted: %s", packet)
		}
	}
}

func TestDualLocalClientUsesOneAreaAndPartyLog(t *testing.T) {
	now := time.Now().UTC()
	engine, err := NewWithBindings([]RoleBinding{
		{Slot: 1, Client: 1, LocalPlayer: 1, ProcessID: 101},
		{Slot: 2, Client: 1, LocalPlayer: 2, ProcessID: 101},
	}, 1, follow.Config{StopDistance: 2, ResumeDistance: 3,
		CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	area := arealog.Signal{AreaID: "G2_4", Seed: 123, ObservedAt: now, Origin: "live"}
	if engine.ObserveClientArea(202, area) {
		t.Fatal("client log was not bound to its process")
	}
	engine.ObserveClientLogLine(101, `Generating level 68 area "G2_4" with seed 123`, now)
	for _, slot := range []int{1, 2} {
		if got := engine.areas[slot]; got.Slot != slot || got.AreaID != area.AreaID || got.Seed != area.Seed {
			t.Fatalf("role %d missed client area: %+v", slot, got)
		}
	}
	newer := area
	newer.ObservedAt = now.Add(time.Second)
	newer.Seed = 456
	if !engine.ObserveClientArea(101, newer) {
		t.Fatal("new client generation was rejected")
	}
	if engine.ObserveClientArea(101, area) || engine.areas[1].Seed != 456 ||
		engine.areas[2].Seed != 456 {
		t.Fatal("old log generation partially replaced a newer client generation")
	}
	if engine.ObserveClientParty(202, partylog.Event{Kind: partylog.Disbanded}) {
		t.Fatal("unbound client event stopped the session")
	}
	engine.ObserveClientLogLine(101, "You have left the party.", now.Add(time.Second))
	if got := engine.Step(now.Add(time.Second))[2]; got.State != follow.Waiting ||
		got.Reason != "local client left party" {
		t.Fatalf("second local role did not stop after the shared log event: %+v", got)
	}
}

func TestOrderedMotionDoesNotRequireSlowVitals(t *testing.T) {
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
		t.Fatal("one client area was not applied")
	}
	motion := OrderedClientFrame{ProcessID: 101, SeenUTC: now.Add(time.Millisecond),
		ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
		AreaHash: 55, AreaInstanceAddress: "0x100000", VectorHeaderAddress: "0x100100",
		OrderedAddresses: []string{"0x200000", "0x300000"},
		CoordinateSpace:  "world", PlayerOrderReliable: true,
		PlayerOrderSource: "experimental_static_vector_0.6.17", Players: []OrderedLocalPlayer{
			{LocalPlayer: 1, X: 30 * worldPerGridUnit},
			{LocalPlayer: 2, X: 0},
		}}
	if !engine.ObserveOrderedClient(motion) {
		t.Fatal("position-only ordered motion was rejected")
	}
	missingIdentity := motion
	missingIdentity.SeenUTC = now.Add(1500 * time.Microsecond)
	missingIdentity.ReceivedAt = missingIdentity.SeenUTC
	missingIdentity.TickMS++
	missingIdentity.OrderedAddresses = []string{"0x200000", "0x200000"}
	if engine.ObserveOrderedClient(missingIdentity) ||
		engine.Step(missingIdentity.SeenUTC)[2].State != follow.Waiting {
		t.Fatal("duplicate static-vector role identities kept old movement active")
	}
	motion.SeenUTC = now.Add(1750 * time.Microsecond)
	motion.ReceivedAt = motion.SeenUTC
	motion.TickMS += 2
	if !engine.ObserveOrderedClient(motion) {
		t.Fatal("complete static-vector identity did not recover after a rejected frame")
	}
	command := engine.Step(motion.SeenUTC)[2]
	if command.State != follow.CatchingUp || command.ManualOverrideEligible {
		t.Fatalf("position-only motion did not follow conservatively: %+v", command)
	}
	broken := motion
	broken.SeenUTC = now.Add(2 * time.Millisecond)
	broken.ReceivedAt = broken.SeenUTC
	broken.TickMS++
	broken.Players = append([]OrderedLocalPlayer(nil), motion.Players...)
	broken.Players[1].Life = floatPtr(50)
	if engine.ObserveOrderedClient(broken) ||
		engine.Step(broken.SeenUTC)[2].State != follow.Waiting {
		t.Fatal("one-sided slow vitality value left old motion active")
	}
	withVitals := motion
	withVitals.SeenUTC = now.Add(3 * time.Millisecond)
	withVitals.ReceivedAt = withVitals.SeenUTC
	withVitals.TickMS += 2
	withVitals.Players = []OrderedLocalPlayer{
		{LocalPlayer: 1, X: 30 * worldPerGridUnit, Life: floatPtr(100), LifeMax: floatPtr(100)},
		{LocalPlayer: 2, X: 0, Life: floatPtr(90), LifeMax: floatPtr(100)},
	}
	if !engine.ObserveOrderedClient(withVitals) ||
		!engine.Step(withVitals.SeenUTC)[2].ManualOverrideEligible {
		t.Fatal("paired vitality values did not restore the full follower state")
	}
	fasterMotion := motion
	fasterMotion.SeenUTC = now.Add(4 * time.Millisecond)
	fasterMotion.ReceivedAt = fasterMotion.SeenUTC
	fasterMotion.TickMS += 3
	if !engine.ObserveOrderedClient(fasterMotion) ||
		!engine.Step(fasterMotion.SeenUTC)[2].ManualOverrideEligible {
		t.Fatal("fresh slower vitality did not survive a position-only motion frame")
	}
	lostOrder := fasterMotion
	lostOrder.SeenUTC = now.Add(5 * time.Millisecond)
	lostOrder.ReceivedAt = lostOrder.SeenUTC
	lostOrder.TickMS++
	lostOrder.PlayerOrderReliable = false
	lostOrder.Players = nil
	if engine.ObserveOrderedClient(lostOrder) {
		t.Fatal("unreliable order was accepted")
	}
	recoveredMotion := motion
	recoveredMotion.SeenUTC = now.Add(6 * time.Millisecond)
	recoveredMotion.ReceivedAt = recoveredMotion.SeenUTC
	recoveredMotion.TickMS += 5
	if !engine.ObserveOrderedClient(recoveredMotion) ||
		engine.Step(recoveredMotion.SeenUTC)[2].ManualOverrideEligible {
		t.Fatal("vitality survived a lost local-player order")
	}
	withVitals.SeenUTC = now.Add(7 * time.Millisecond)
	withVitals.ReceivedAt = withVitals.SeenUTC
	withVitals.TickMS += 4
	if !engine.ObserveOrderedClient(withVitals) ||
		!engine.Step(withVitals.SeenUTC)[2].ManualOverrideEligible {
		t.Fatal("fresh vitality did not restore after order recovery")
	}
	afterExpiry := motion
	afterExpiry.SeenUTC = now.Add(2 * time.Second)
	afterExpiry.ReceivedAt = afterExpiry.SeenUTC
	afterExpiry.TickMS += 2000
	if !engine.ObserveOrderedClient(afterExpiry) {
		t.Fatal("motion after vital expiry was rejected")
	}
	if command := engine.Step(afterExpiry.SeenUTC)[2]; command.State != follow.CatchingUp ||
		command.ManualOverrideEligible {
		t.Fatalf("expired vitality still authorized manual movement: %+v", command)
	}
	newArea := afterExpiry.SeenUTC.Add(time.Millisecond)
	if !engine.ObserveClientArea(101, arealog.Signal{AreaID: "G2_5", Seed: 456,
		ObservedAt: newArea, Origin: "live"}) {
		t.Fatal("new area rejected")
	}
	afterArea := motion
	afterArea.SeenUTC = newArea.Add(time.Millisecond)
	afterArea.ReceivedAt = afterArea.SeenUTC
	afterArea.TickMS += 2002
	if !engine.ObserveOrderedClient(afterArea) ||
		engine.Step(afterArea.SeenUTC)[2].ManualOverrideEligible {
		t.Fatal("old vitality crossed into a new area")
	}
}

func TestExperimentalOrderedRolesStopOnSameAreaReorder(t *testing.T) {
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
		t.Fatal("client area was not applied")
	}
	frame := OrderedClientFrame{ProcessID: 101, SeenUTC: now, ReceivedAt: now,
		TickMS: now.UnixMilli(), AreaHash: 55, AreaInstanceAddress: "0x100000",
		VectorHeaderAddress: "0x100100", OrderedAddresses: []string{"0x200000", "0x300000"},
		CoordinateSpace: "world", PlayerOrderReliable: true,
		PlayerOrderSource: experimentalStaticVectorSource,
		Players: []OrderedLocalPlayer{{LocalPlayer: 1, X: 30 * worldPerGridUnit},
			{LocalPlayer: 2, X: 0}}}
	if !engine.ObserveOrderedClient(frame) || engine.Step(now)[2].State != follow.CatchingUp {
		t.Fatal("initial ordered pair did not follow")
	}
	swapped := frame
	swapped.SeenUTC = now.Add(time.Millisecond)
	swapped.ReceivedAt = swapped.SeenUTC
	swapped.TickMS++
	swapped.OrderedAddresses = []string{"0x300000", "0x200000"}
	swapped.Players = []OrderedLocalPlayer{{LocalPlayer: 1, X: 0},
		{LocalPlayer: 2, X: 30 * worldPerGridUnit}}
	if engine.ObserveOrderedClient(swapped) ||
		engine.Step(swapped.SeenUTC)[2].State != follow.Waiting {
		t.Fatal("same-area role reorder kept follower movement active")
	}
	swapped.SeenUTC = now.Add(2 * time.Millisecond)
	swapped.ReceivedAt = swapped.SeenUTC
	swapped.TickMS++
	if engine.ObserveOrderedClient(swapped) {
		t.Fatal("repeated reordered frame silently became the new role binding")
	}
	restored := frame
	restored.SeenUTC = now.Add(3 * time.Millisecond)
	restored.ReceivedAt = restored.SeenUTC
	restored.TickMS += 3
	if !engine.ObserveOrderedClient(restored) ||
		engine.Step(restored.SeenUTC)[2].State != follow.CatchingUp {
		t.Fatal("original role order did not recover")
	}
	if !engine.ObserveClientArea(101, arealog.Signal{AreaID: "G2_5", Seed: 456,
		ObservedAt: now.Add(4 * time.Millisecond), Origin: "live"}) {
		t.Fatal("new client area was not applied")
	}
	swapped.SeenUTC = now.Add(5 * time.Millisecond)
	swapped.ReceivedAt = swapped.SeenUTC
	swapped.TickMS += 3
	swapped.AreaHash = 56
	swapped.AreaInstanceAddress = "0x110000"
	if !engine.ObserveOrderedClient(swapped) {
		t.Fatal("new area instance could not establish its own ordered addresses")
	}
	if !engine.ObserveClientArea(101, arealog.Signal{AreaID: "G2_4", Seed: 789,
		ObservedAt: now.Add(6 * time.Millisecond), Origin: "live"}) {
		t.Fatal("new log generation with a recycled area address was rejected")
	}
	reusedArea := frame
	reusedArea.SeenUTC = now.Add(7 * time.Millisecond)
	reusedArea.ReceivedAt = reusedArea.SeenUTC
	reusedArea.TickMS += 7
	reusedArea.AreaHash = swapped.AreaHash
	reusedArea.AreaInstanceAddress = swapped.AreaInstanceAddress
	if !engine.ObserveOrderedClient(reusedArea) ||
		engine.Step(reusedArea.SeenUTC)[2].State != follow.CatchingUp {
		t.Fatal("new generation could not resume following at a reused area address")
	}
	reorderedAgain := swapped
	reorderedAgain.SeenUTC = now.Add(8 * time.Millisecond)
	reorderedAgain.ReceivedAt = reorderedAgain.SeenUTC
	reorderedAgain.TickMS += 7
	if engine.ObserveOrderedClient(reorderedAgain) ||
		engine.Step(reorderedAgain.SeenUTC)[2].State != follow.Waiting {
		t.Fatal("new generation allowed same-generation role reorder to drive movement")
	}
}

func TestUnverifiedSlotCandidateCannotBePromotedByReliableFlag(t *testing.T) {
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
		t.Fatal("client area was not applied")
	}
	frame := OrderedClientFrame{ProcessID: 101, SeenUTC: now.Add(time.Millisecond),
		ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
		CoordinateSpace: "world", PlayerOrderReliable: true,
		PlayerOrderSource: "verified fixture", Players: []OrderedLocalPlayer{
			{LocalPlayer: 1, X: 30 * worldPerGridUnit},
			{LocalPlayer: 2, X: 0},
		}}
	if !engine.ObserveOrderedClient(frame) || engine.Step(frame.SeenUTC)[2].State != follow.CatchingUp {
		t.Fatal("verified fixture did not start following")
	}
	frame.SeenUTC = now.Add(2 * time.Millisecond)
	frame.ReceivedAt = frame.SeenUTC
	frame.TickMS++
	frame.PlayerOrderSource = " UNVERIFIED_EXILECORE_LOCAL_SLOT_CANDIDATE "
	if engine.ObserveOrderedClient(frame) || engine.Step(frame.SeenUTC)[2].State != follow.Waiting {
		t.Fatal("unverified local-slot diagnostic drove or preserved follow movement")
	}
}

func TestTwoLocalPlayersNeedOneCompleteOrderedFrame(t *testing.T) {
	now := time.Now().UTC()
	bindings := []RoleBinding{
		{Slot: 1, Client: 1, LocalPlayer: 1, ProcessID: 101},
		{Slot: 2, Client: 1, LocalPlayer: 2, ProcessID: 101},
	}
	engine, err := NewWithBindings(bindings, 1, follow.Config{
		StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10,
		MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	for _, slot := range []int{1, 2} {
		if !engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: now, Origin: "live"}) {
			t.Fatal("role area was not accepted")
		}
	}
	if engine.ObserveSample(telemetry.Sample{ProcessID: 101, SeenUTC: now.Add(time.Millisecond),
		ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
		CoordinateSpace: "world", X: 12 * worldPerGridUnit}) {
		t.Fatal("single-player sensor sample assigned a dual-local client")
	}
	full := OrderedClientFrame{ProcessID: 101, SeenUTC: now.Add(time.Millisecond),
		ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
		CoordinateSpace: "world", PlayerOrderReliable: true,
		PlayerOrderSource: "validated local-player vector", Players: []OrderedLocalPlayer{
			{LocalPlayer: 1, X: 12 * worldPerGridUnit, Life: floatPtr(100), LifeMax: floatPtr(100)},
			{LocalPlayer: 2, X: 0, Life: floatPtr(90), LifeMax: floatPtr(100)},
		}}
	if !engine.ObserveOrderedClient(full) {
		t.Fatal("complete two-local frame was rejected")
	}
	if got := engine.Step(now.Add(time.Millisecond))[2]; got.State != follow.CatchingUp {
		t.Fatalf("second local role did not receive a follow decision: %+v", got)
	}
	bad := full
	bad.SeenUTC = now.Add(2 * time.Millisecond)
	bad.ReceivedAt = bad.SeenUTC
	bad.TickMS++
	bad.Players = bad.Players[:1]
	if engine.ObserveOrderedClient(bad) || len(engine.samples) != 0 {
		t.Fatalf("incomplete current frame left an old local role active: %+v", engine.samples)
	}
	if got := engine.Step(now.Add(2 * time.Millisecond))[2]; got.State != follow.Waiting {
		t.Fatalf("role moved after order became incomplete: %+v", got)
	}
	full.SeenUTC = now.Add(3 * time.Millisecond)
	full.ReceivedAt = full.SeenUTC
	full.TickMS += 2
	if !engine.ObserveOrderedClient(full) || engine.Step(full.SeenUTC)[2].State != follow.CatchingUp {
		t.Fatal("complete order did not restore follow after a dropped frame")
	}
	old := bad
	old.PlayerOrderReliable = false
	if engine.ObserveOrderedClient(old) || len(engine.samples) != 2 {
		t.Fatal("an old unreliable frame erased newer ordered positions")
	}
	current := full
	current.SeenUTC = now.Add(4 * time.Millisecond)
	current.ReceivedAt = current.SeenUTC
	current.TickMS++
	current.PlayerOrderReliable = false
	if engine.ObserveOrderedClient(current) || len(engine.samples) != 0 ||
		engine.Step(current.SeenUTC)[2].State != follow.Waiting {
		t.Fatal("current loss of player order did not release both local roles")
	}
}

func TestFourRolesAcrossTwoDualLocalClients(t *testing.T) {
	now := time.Now().UTC()
	bindings := []RoleBinding{
		{1, 1, 1, 101}, {2, 1, 2, 101},
		{3, 2, 1, 202}, {4, 2, 2, 202},
	}
	engine, err := NewWithBindings(bindings, 1, follow.Config{
		StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10,
		MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	for slot := 1; slot <= 4; slot++ {
		engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: now, Origin: "live"})
	}
	frame := func(pid int, x1, x2 float64) OrderedClientFrame {
		return OrderedClientFrame{ProcessID: pid, SeenUTC: now.Add(time.Millisecond),
			ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
			CoordinateSpace: "world", PlayerOrderReliable: true,
			PlayerOrderSource: "validated local-player vector", Players: []OrderedLocalPlayer{
				{LocalPlayer: 1, X: x1 * worldPerGridUnit, Life: floatPtr(100), LifeMax: floatPtr(100)},
				{LocalPlayer: 2, X: x2 * worldPerGridUnit, Life: floatPtr(100), LifeMax: floatPtr(100)},
			}}
	}
	if !engine.ObserveOrderedClient(frame(101, 30, 20)) ||
		!engine.ObserveOrderedClient(frame(202, 10, 0)) {
		t.Fatal("two complete client frames were not accepted")
	}
	commands := engine.Step(now.Add(time.Millisecond))
	for _, slot := range []int{2, 3, 4} {
		if commands[slot].State != follow.CatchingUp {
			t.Fatalf("P%d did not follow across the two-client layout: %+v", slot, commands[slot])
		}
	}
}

func TestSixRolesAcrossThreeDualLocalClients(t *testing.T) {
	now := time.Now().UTC()
	bindings := []RoleBinding{
		{1, 1, 1, 101}, {2, 1, 2, 101},
		{3, 2, 1, 202}, {4, 2, 2, 202},
		{5, 3, 1, 303}, {6, 3, 2, 303},
	}
	engine, err := NewWithBindings(bindings, 1, follow.Config{
		StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10,
		MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	for slot := 1; slot <= 6; slot++ {
		if !engine.ObserveArea(arealog.Signal{Slot: slot, AreaID: "G2_4", Seed: 123,
			ObservedAt: now, Origin: "live"}) {
			t.Fatal("area rejected")
		}
	}
	for client := 1; client <= 3; client++ {
		pid := client * 101
		x := float64(70 - client*20)
		if !engine.ObserveOrderedClient(OrderedClientFrame{
			ProcessID: pid, SeenUTC: now.Add(time.Millisecond),
			ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
			CoordinateSpace: "world", PlayerOrderReliable: true,
			PlayerOrderSource: "validated local-player vector",
			Players: []OrderedLocalPlayer{
				{LocalPlayer: 1, X: x * worldPerGridUnit, Life: floatPtr(100), LifeMax: floatPtr(100)},
				{LocalPlayer: 2, X: (x - 10) * worldPerGridUnit, Life: floatPtr(100), LifeMax: floatPtr(100)},
			},
		}) {
			t.Fatalf("client %d frame rejected", client)
		}
	}
	commands := engine.Step(now.Add(time.Millisecond))
	for slot := 2; slot <= 6; slot++ {
		if commands[slot].State != follow.CatchingUp {
			t.Fatalf("P%d was not represented in six-role follow: %+v", slot, commands[slot])
		}
	}
}

func TestFiveRolesMixDualAndStandaloneClientPositions(t *testing.T) {
	for _, layout := range []struct {
		name     string
		bindings []RoleBinding
	}{
		{"third-solo", []RoleBinding{{1, 1, 1, 101}, {2, 1, 2, 101},
			{3, 2, 1, 202}, {4, 2, 2, 202}, {5, 3, 1, 303}}},
		{"main-solo", []RoleBinding{{1, 1, 1, 101}, {2, 2, 1, 202},
			{3, 2, 2, 202}, {4, 3, 1, 303}, {5, 3, 2, 303}}},
	} {
		t.Run(layout.name, func(t *testing.T) {
			now := time.Now().UTC()
			engine, err := NewWithBindings(layout.bindings, 1, follow.Config{
				StopDistance: 2, ResumeDistance: 3, CatchUpDistance: 10,
				MaxAge: time.Second}, time.Minute)
			if err != nil {
				t.Fatal(err)
			}
			defer engine.Close()
			for _, pid := range []int{101, 202, 303} {
				if !engine.ObserveClientArea(pid, arealog.Signal{AreaID: "G2_4", Seed: 123,
					ObservedAt: now, Origin: "live"}) {
					t.Fatalf("client %d area rejected", pid)
				}
			}
			for _, pid := range []int{101, 202, 303} {
				roles := engine.pidToRoles[pid]
				if len(roles) == 1 {
					role := roles[0]
					sample := telemetry.Sample{ProcessID: pid, SeenUTC: now.Add(time.Millisecond),
						ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
						CoordinateSpace: "world", X: float64(60-role.Slot*10) * worldPerGridUnit}
					if !engine.ObserveSample(sample) {
						t.Fatalf("solo client %d position rejected", pid)
					}
					continue
				}
				players := make([]OrderedLocalPlayer, 0, len(roles))
				for _, role := range roles {
					players = append(players, OrderedLocalPlayer{LocalPlayer: role.LocalPlayer,
						X: float64(60-role.Slot*10) * worldPerGridUnit})
				}
				frame := OrderedClientFrame{ProcessID: pid, SeenUTC: now.Add(time.Millisecond),
					ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
					AreaHash: 55, AreaInstanceAddress: "0x100000", VectorHeaderAddress: "0x100100",
					OrderedAddresses: []string{"0x200000", "0x300000"},
					CoordinateSpace:  "world", PlayerOrderReliable: true,
					PlayerOrderSource: "experimental_static_vector_0.6.17", Players: players}
				if !engine.ObserveOrderedClient(frame) || engine.ObserveSample(telemetry.Sample{
					ProcessID: pid, SeenUTC: frame.SeenUTC, ReceivedAt: frame.ReceivedAt,
					TickMS: frame.TickMS, CoordinateSpace: "world"}) {
					t.Fatalf("dual client %d accepted ordinary position or rejected ordered frame", pid)
				}
			}
			commands := engine.Step(now.Add(time.Millisecond))
			for slot := 2; slot <= 5; slot++ {
				if commands[slot].State != follow.CatchingUp {
					t.Fatalf("P%d did not follow in mixed layout: %+v", slot, commands[slot])
				}
			}
		})
	}
}

func TestLogRotationRevokesBothLocalRolesUntilNewGeneration(t *testing.T) {
	now := time.Now().UTC().Truncate(time.Millisecond)
	engine, err := NewWithBindings([]RoleBinding{
		{Slot: 1, Client: 1, LocalPlayer: 1, ProcessID: 101},
		{Slot: 2, Client: 1, LocalPlayer: 2, ProcessID: 101},
		{Slot: 3, Client: 2, LocalPlayer: 1, ProcessID: 303},
	}, 1, follow.Config{StopDistance: 2, ResumeDistance: 3,
		CatchUpDistance: 10, MaxAge: time.Second}, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	for _, pid := range []int{101, 303} {
		if !engine.ObserveClientArea(pid, arealog.Signal{AreaID: "G2_4", Seed: 123,
			ObservedAt: now, Origin: "live"}) {
			t.Fatal("initial client area was rejected")
		}
	}
	frame := OrderedClientFrame{ProcessID: 101, SeenUTC: now.Add(time.Millisecond),
		ReceivedAt: now.Add(time.Millisecond), TickMS: now.UnixMilli() + 1,
		AreaHash: 55, AreaInstanceAddress: "0x100000", VectorHeaderAddress: "0x100100",
		OrderedAddresses: []string{"0x200000", "0x300000"},
		CoordinateSpace:  "world", PlayerOrderReliable: true,
		PlayerOrderSource: "experimental_static_vector_0.6.17",
		Players: []OrderedLocalPlayer{
			{LocalPlayer: 1, X: 30 * worldPerGridUnit},
			{LocalPlayer: 2, X: 0},
		}}
	if !engine.ObserveOrderedClient(frame) || !observeGridSample(engine, telemetry.Sample{
		ProcessID: 303, SeenUTC: frame.SeenUTC, X: 0, AreaHash: 66}) ||
		engine.Step(frame.SeenUTC)[2].State != follow.CatchingUp {
		t.Fatal("initial two-local follow did not start")
	}
	if !engine.InvalidateClientArea(101) || engine.InvalidateClientArea(999) ||
		engine.Step(now.Add(2 * time.Millisecond))[2].State != follow.Waiting ||
		engine.Step(now.Add(2 * time.Millisecond))[3].State != follow.Waiting ||
		len(engine.areas) != 1 || len(engine.samples) != 1 {
		t.Fatal("rotation retained one local role or unrelated client was revoked")
	}
	if !engine.ObserveClientArea(101, arealog.Signal{AreaID: "G2_4", Seed: 123,
		ObservedAt: now.Add(3 * time.Millisecond), Origin: "live"}) {
		t.Fatal("new log generation was rejected")
	}
	frame.SeenUTC = now.Add(4 * time.Millisecond)
	frame.ReceivedAt = frame.SeenUTC
	frame.TickMS += 3
	if !engine.ObserveOrderedClient(frame) || engine.Step(frame.SeenUTC)[2].State != follow.CatchingUp {
		t.Fatal("fresh log and motion did not restore two-local follow")
	}
}

func TestOrderedClientRejectsUnverifiedAndAmbiguousBindings(t *testing.T) {
	config := follow.Config{StopDistance: 2, ResumeDistance: 3,
		CatchUpDistance: 10, MaxAge: time.Second}
	for _, bindings := range [][]RoleBinding{
		{{1, 1, 1, 101}, {2, 1, 1, 101}},
		{{1, 1, 1, 101}, {2, 2, 1, 101}},
		{{1, 1, 1, 101}, {2, 3, 1, 202}},
	} {
		if engine, err := NewWithBindings(bindings, 1, config, time.Minute); err == nil {
			engine.Close()
			t.Fatalf("ambiguous role binding accepted: %+v", bindings)
		}
	}
	engine, err := NewWithBindings([]RoleBinding{{1, 1, 1, 101}, {2, 1, 2, 101}},
		1, config, time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	defer engine.Close()
	now := time.Now().UTC()
	frame := OrderedClientFrame{ProcessID: 101, SeenUTC: now,
		ReceivedAt: now, TickMS: now.UnixMilli(), CoordinateSpace: "world",
		PlayerOrderSource: "candidate scan", Players: []OrderedLocalPlayer{
			{LocalPlayer: 1, Life: floatPtr(100), LifeMax: floatPtr(100)},
			{LocalPlayer: 2, Life: floatPtr(100), LifeMax: floatPtr(100)},
		}}
	if engine.ObserveOrderedClient(frame) {
		t.Fatal("unreliable order drove positions")
	}
	frame.PlayerOrderReliable = true
	frame.Players[1].Life = nil
	if engine.ObserveOrderedClient(frame) {
		t.Fatal("missing second-role life was accepted")
	}
}

func floatPtr(value float64) *float64 { return &value }
