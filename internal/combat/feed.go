package combat

import (
	"errors"
	"fmt"
	"math"
	"strings"
	"time"
)

// FeedFrame is a verified ordered-client input contract for read-only combat
// replay. Current PartyPilot sensor diagnostics do not produce this contract.
type FeedFrame struct {
	Client              int           `json:"client"`
	CapturedAt          time.Time     `json:"capturedAt"`
	AreaInstance        string        `json:"areaInstance"`
	AreaID              string        `json:"areaId"`
	AreaLabel           string        `json:"areaLabel"`
	Attached            bool          `json:"attached"`
	PlayerOrderReliable bool          `json:"playerOrderReliable"`
	UI                  UI            `json:"ui"`
	Players             []FeedPlayer  `json:"players"`
	Monsters            []FeedMonster `json:"monsters"`
}

type FeedPlayer struct {
	LocalPlayer   int      `json:"localPlayer"`
	Position      *Point   `json:"position"`
	Alive         *bool    `json:"alive"`
	HealthPercent *float64 `json:"healthPercent"`
}

type FeedMonster struct {
	ID            string   `json:"id"`
	Rarity        string   `json:"rarity"`
	Position      *Point   `json:"position"`
	Alive         *bool    `json:"alive"`
	HealthPercent *float64 `json:"healthPercent"`
}

type FeedAuthority struct {
	ExpectedClients []int        `json:"expectedClients"`
	EngagedPlayers  map[int]bool `json:"engagedPlayers"`
	SafeZone        *bool        `json:"safeZone"`
	EmergencyActive *bool        `json:"emergencyActive"`
}

// AssembleWorld follows the supplied adapter's three-client P1-P6 mapping,
// while requiring every requested client and safety field to be present.
func AssembleWorld(frames []FeedFrame, authority FeedAuthority, now time.Time) (World, error) {
	if now.IsZero() || len(authority.ExpectedClients) == 0 || len(authority.ExpectedClients) > 3 ||
		authority.SafeZone == nil || authority.EmergencyActive == nil || authority.EngagedPlayers == nil {
		return World{}, errors.New("combat feed authority is incomplete")
	}
	for index, client := range authority.ExpectedClients {
		if client != index+1 {
			return World{}, errors.New("expected combat clients must be 1 through N")
		}
	}
	if len(frames) != len(authority.ExpectedClients) {
		return World{}, errors.New("combat feed is missing a requested client")
	}
	byClient := make(map[int]FeedFrame, len(frames))
	for _, frame := range frames {
		if frame.Client < 1 || frame.Client > len(authority.ExpectedClients) ||
			!frame.Attached || !frame.PlayerOrderReliable || frame.CapturedAt.IsZero() ||
			strings.TrimSpace(frame.AreaInstance) == "" {
			return World{}, errors.New("combat feed client identity or ordered players are unavailable")
		}
		if _, exists := byClient[frame.Client]; exists {
			return World{}, errors.New("duplicate combat feed client")
		}
		byClient[frame.Client] = frame
	}
	primary := byClient[1]
	if primary.CapturedAt.After(now) || primary.Monsters == nil {
		return World{}, errors.New("primary combat feed time or monster scan is unavailable")
	}
	players := make(map[int]Player, len(frames)*2)
	for _, client := range authority.ExpectedClients {
		frame := byClient[client]
		if !sameSemanticArea(primary, frame) || len(frame.Players) < 1 || len(frame.Players) > 2 ||
			frame.UI.Loading == nil {
			return World{}, fmt.Errorf("client %d area, loading, or player order is incomplete", client)
		}
		seenLocal := make(map[int]bool, 2)
		for _, observed := range frame.Players {
			if observed.LocalPlayer < 1 || observed.LocalPlayer > 2 || seenLocal[observed.LocalPlayer] ||
				observed.Position == nil || observed.Alive == nil ||
				!finite(float64(observed.Position.X)) || !finite(float64(observed.Position.Y)) {
				return World{}, fmt.Errorf("client %d has an invalid local player", client)
			}
			seenLocal[observed.LocalPlayer] = true
			playerNumber := (client-1)*2 + observed.LocalPlayer
			engaged, ok := authority.EngagedPlayers[playerNumber]
			if !ok {
				return World{}, fmt.Errorf("P%d engaged state is missing", playerNumber)
			}
			players[playerNumber] = Player{Position: observed.Position, Alive: observed.Alive,
				Loading: frame.UI.Loading, ActiveCombo: &engaged,
				HealthPercent: observed.HealthPercent}
		}
		for local := 1; local <= len(frame.Players); local++ {
			if !seenLocal[local] {
				return World{}, fmt.Errorf("client %d has a gap in ordered local players", client)
			}
		}
	}
	main, ok := players[1]
	if !ok {
		return World{}, errors.New("main player is unavailable")
	}
	for number, player := range players {
		distance := math.Hypot(float64(player.Position.X-main.Position.X),
			float64(player.Position.Y-main.Position.Y))
		player.DistanceFromMainM = &distance
		players[number] = player
	}
	monsters := make([]Monster, 0, len(primary.Monsters))
	for _, source := range primary.Monsters {
		if source.Alive == nil {
			return World{}, errors.New("primary monster evidence is incomplete")
		}
		if !*source.Alive {
			continue
		}
		if source.Position == nil || source.HealthPercent == nil {
			return World{}, errors.New("living monster evidence is incomplete")
		}
		byPlayer := make(map[int]float64, len(players))
		for number, player := range players {
			byPlayer[number] = math.Hypot(float64(source.Position.X-player.Position.X),
				float64(source.Position.Y-player.Position.Y))
		}
		mainDistance := byPlayer[1]
		monsters = append(monsters, Monster{ID: source.ID, Rarity: source.Rarity,
			Alive: source.Alive, Position: source.Position, HealthPercent: source.HealthPercent,
			DistanceFromMainM: &mainDistance, DistanceByPlayer: byPlayer})
	}
	age := int(now.Sub(primary.CapturedAt) / time.Millisecond)
	world := World{Timestamp: primary.CapturedAt, AreaInstance: primary.AreaInstance,
		AreaID: primary.AreaID, AreaLabel: primary.AreaLabel, DataAgeMS: &age,
		SafeZone: authority.SafeZone, EmergencyActive: authority.EmergencyActive,
		UI: primary.UI, Players: players, Monsters: monsters}
	if err := world.Validate(); err != nil {
		return World{}, err
	}
	return world, nil
}

func sameSemanticArea(primary, candidate FeedFrame) bool {
	if primary.Client == candidate.Client {
		return true
	}
	if primary.AreaID != "" && candidate.AreaID != "" {
		return strings.EqualFold(primary.AreaID, candidate.AreaID)
	}
	if primary.AreaLabel != "" && candidate.AreaLabel != "" {
		return strings.EqualFold(primary.AreaLabel, candidate.AreaLabel)
	}
	return primary.AreaInstance != "" && strings.EqualFold(primary.AreaInstance, candidate.AreaInstance)
}
