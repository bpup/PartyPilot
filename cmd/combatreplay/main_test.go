package main

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"

	"partypilot/internal/combat"
)

func TestLoadProfileKeepsExplicitRoleEligibility(t *testing.T) {
	profile := combat.DefaultProfile()
	profile.Roles[1].Logic.AttackNormal = false
	profile.Roles[1].Logic.StartHelpingAtHealthPercent = 40
	data, err := json.Marshal(profile)
	if err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(t.TempDir(), "profile.json")
	if err := os.WriteFile(path, data, 0600); err != nil {
		t.Fatal(err)
	}
	loaded, err := loadProfile(path)
	if err != nil {
		t.Fatal(err)
	}
	if loaded.Roles[1].Logic.AttackNormal ||
		loaded.Roles[1].Logic.StartHelpingAtHealthPercent != 40 {
		t.Fatalf("role eligibility was lost: %+v", loaded.Roles[1].Logic)
	}
	if err := os.WriteFile(path, append(data, []byte(`{}`)...), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := loadProfile(path); err == nil {
		t.Fatal("trailing JSON was accepted as a profile")
	}
}
