package combat

// Default data is independently represented from the supplied 0.6.17 profile.
// It describes candidate plans only; no game input is emitted from this package.
type Profile struct {
	Enabled bool    `json:"enabled"`
	Safety  Safety  `json:"safety"`
	Roles   []Role  `json:"roles"`
	Combos  []Combo `json:"combos"`
}

type Safety struct {
	StaleDataTimeoutMS int  `json:"staleDataTimeoutMs"`
	PauseAnyMenu       bool `json:"pauseAnyMenu"`
	PauseChat          bool `json:"pauseChat"`
	PauseLoading       bool `json:"pauseLoading"`
	PauseDead          bool `json:"pauseDead"`
	PauseInventory     bool `json:"pauseInventoryStashVendor"`
}

type Role struct {
	Player          int               `json:"player"`
	Name            string            `json:"name"`
	Kind            string            `json:"kind"`
	Anchor          string            `json:"anchor"`
	Behaviors       map[string]string `json:"behaviors"`
	Targets         map[string]string `json:"targets"`
	SkillButtons    map[int]string    `json:"skillButtons"`
	PressMS         int               `json:"pressMs"`
	RoleOffsetMinMS int               `json:"roleOffsetMinMs"`
	RoleOffsetMaxMS int               `json:"roleOffsetMaxMs"`
}

type Combo struct {
	ID       string `json:"id"`
	Category string `json:"category"`
	Repeat   string `json:"repeat"`
	Steps    []Step `json:"steps"`
}

type Step struct {
	ID         string `json:"id"`
	Action     string `json:"action"`
	SkillSlot  int    `json:"skillSlot,omitempty"`
	Condition  string `json:"condition"`
	DelayMin   int    `json:"delayMinMs"`
	DelayMax   int    `json:"delayMaxMs"`
	DurationMS int    `json:"durationMs,omitempty"`
}

func DefaultProfile() Profile {
	buttons := func() map[int]string {
		return map[int]string{1: "X", 2: "Y", 3: "RB", 4: "RT", 5: "LB"}
	}
	roles := []Role{
		{1, "Main DPS", "main-dps", "independent-in-combat",
			map[string]string{"pack": "primary-clear", "rare": "rare-dps", "boss": "boss-engage", "emergency": "emergency-defence"},
			map[string]string{"pack": "highest-threat", "rare": "highest-threat", "boss": "unique", "emergency": "none"}, buttons(), 85, 0, 0},
		{2, "Companion A", "companion-dps", "main",
			map[string]string{"pack": "companion-rally-clear", "rare": "companion-rally-rare", "boss": "companion-rally-boss", "emergency": "companion-recovery"},
			map[string]string{"pack": "nearest-valid", "rare": "assigned-target", "boss": "assigned-target", "emergency": "none"}, buttons(), 85, 20, 80},
		{3, "Companion B", "companion-dps", "main",
			map[string]string{"pack": "companion-rally-clear", "rare": "companion-rally-rare", "boss": "companion-rally-boss", "emergency": "companion-recovery"},
			map[string]string{"pack": "nearest-valid", "rare": "assigned-target", "boss": "assigned-target", "emergency": "none"}, buttons(), 85, 35, 95},
		{4, "Support", "support", "main",
			map[string]string{"pack": "pack-support", "rare": "rare-support", "boss": "boss-support", "emergency": "emergency-support"},
			map[string]string{"pack": "main-target", "rare": "main-target", "boss": "main-target", "emergency": "party-centre"}, buttons(), 85, 90, 220},
		{5, "Defence", "defence", "party-centre",
			map[string]string{"pack": "defensive-standby", "rare": "defensive-setup", "boss": "boss-defence", "emergency": "emergency-defence"},
			map[string]string{"pack": "party-centre", "rare": "party-centre", "boss": "party-centre", "emergency": "party-centre"}, buttons(), 85, 140, 280},
		{6, "Rarity Finisher", "rarity-finisher", "main",
			map[string]string{"pack": "safe-follow", "rare": "finisher-ready", "boss": "finisher-ready", "emergency": "escape-return"},
			map[string]string{"pack": "none", "rare": "main-target", "boss": "main-target", "emergency": "none"}, buttons(), 85, 180, 360},
	}
	skill := func(id string, slot int, condition string, min, max int) Step {
		return Step{ID: id, Action: "skill", SkillSlot: slot, Condition: condition, DelayMin: min, DelayMax: max}
	}
	swap := func(id, condition string, min, max int) Step {
		return Step{ID: id, Action: "weapon-swap", Condition: condition, DelayMin: min, DelayMax: max}
	}
	action := func(id, name, condition string, min, max int) Step {
		return Step{ID: id, Action: name, Condition: condition, DelayMin: min, DelayMax: max}
	}
	combos := []Combo{
		{"primary-clear", "pack", "while-engaged", []Step{skill("pc-1", 1, "enemy-present", 70, 140)}},
		{"rare-dps", "rare", "while-engaged", []Step{skill("rd-1", 3, "rare-present", 90, 160)}},
		{"boss-engage", "boss", "once-per-target", []Step{skill("be-1", 4, "unique-present", 80, 150)}},
		{"companion-rally-clear", "pack", "once-per-encounter", []Step{swap("crc-1", "always", 90, 160), swap("crc-2", "always", 120, 190), skill("crc-3", 1, "enemy-present", 130, 240)}},
		{"companion-rally-rare", "rare", "once-per-target", []Step{swap("crr-1", "rare-present", 90, 160), swap("crr-2", "rare-present", 120, 190), skill("crr-3", 3, "target-alive", 140, 260)}},
		{"companion-rally-boss", "boss", "once-per-target", []Step{swap("crb-1", "unique-present", 90, 160), swap("crb-2", "unique-present", 120, 190), skill("crb-3", 4, "target-alive", 140, 260)}},
		{"pack-support", "support", "once-per-encounter", []Step{skill("ps-1", 3, "party-engaged", 170, 280)}},
		{"rare-support", "support", "once-per-encounter", []Step{skill("rs-1", 3, "rare-present", 160, 270)}},
		{"boss-support", "support", "once-per-encounter", []Step{skill("bs-1", 4, "unique-present", 150, 260)}},
		{"defensive-standby", "defence", "do-not-repeat", []Step{{ID: "ds-1", Action: "wait", Condition: "always", DelayMin: 0, DelayMax: 20, DurationMS: 250}}},
		{"defensive-setup", "defence", "once-per-encounter", []Step{skill("dset-1", 3, "rare-present", 180, 300)}},
		{"boss-defence", "defence", "once-per-encounter", []Step{skill("bd-1", 4, "unique-present", 180, 320)}},
		{"emergency-defence", "emergency", "cooldown", []Step{skill("ed-1", 5, "emergency-active", 30, 80)}},
		{"companion-recovery", "emergency", "cooldown", []Step{swap("cor-1", "emergency-active", 40, 90), swap("cor-2", "always", 110, 180)}},
		{"emergency-support", "emergency", "cooldown", []Step{skill("es-1", 5, "emergency-active", 40, 90)}},
		{"safe-follow", "movement", "do-not-repeat", []Step{action("sf-1", "follow-safe-anchor", "always", 140, 250)}},
		{"finisher-ready", "finisher", "once-per-target", []Step{action("fr-1", "hold-safe-position", "always", 160, 280), skill("fr-2", 4, "finisher-condition", 90, 170)}},
		{"escape-return", "emergency", "cooldown", []Step{skill("er-1", 5, "emergency-active", 35, 85), action("er-2", "return-to-anchor", "danger-remains", 100, 190)}},
	}
	return Profile{false, Safety{750, true, true, true, true, true}, roles, combos}
}
