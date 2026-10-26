package resourcepolicy

import "testing"

func TestForegroundRestorationPlan(t *testing.T) {
	policy := Policy{CPUPercent: 25, RAMWarningMB: 4096}
	if err := policy.Validate(); err != nil {
		t.Fatal(err)
	}
	if Decide(policy, false, false) != ApplyLimits || Decide(policy, false, true) != NoChange ||
		Decide(policy, true, true) != RestoreLimits || Decide(policy, true, false) != NoChange {
		t.Fatal("foreground/background transitions are incorrect")
	}
	warningOnly := Policy{RAMWarningMB: 4096}
	if Decide(warningOnly, false, false) != NoChange {
		t.Fatal("RAM warning was treated as a hard limit")
	}
	if err := (Policy{CPUPercent: 101}).Validate(); err == nil {
		t.Fatal("invalid CPU cap accepted")
	}
}
