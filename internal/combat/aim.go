package combat

import "math"

// AimAt mirrors the target version's normalized right-stick aim calculation.
// It returns nil when the read-only replay lacks a verified player position or target.
func AimAt(world World, playerNumber int, targetID string) *Aim {
	player, ok := world.Players[playerNumber]
	if !ok || player.Position == nil || targetID == "" {
		return nil
	}
	for _, monster := range world.Monsters {
		if monster.ID != targetID || monster.Alive == nil || !*monster.Alive || monster.Position == nil {
			continue
		}
		dx := float64(monster.Position.X - player.Position.X)
		dy := float64(monster.Position.Y - player.Position.Y)
		if !finite(dx) || !finite(dy) {
			return nil
		}
		lengthSquared := dx*dx + dy*dy
		if lengthSquared < 0.001 {
			return &Aim{}
		}
		length := math.Sqrt(lengthSquared)
		return &Aim{
			RightX: int16(max(-32768, min(32767, math.Round(dx/length*32767)))),
			RightY: int16(max(-32768, min(32767, math.Round(dy/length*32767)))),
		}
	}
	return nil
}
