package resourcepolicy

import "errors"

type Policy struct {
	CPUPercent   int    `json:"background_cpu_percent"`
	AffinityMask uint64 `json:"background_affinity_mask"`
	RAMWarningMB int    `json:"ram_warning_mb"`
	RAMLimitMB   int    `json:"background_ram_limit_mb"`
}

func (policy Policy) Validate() error {
	if policy.CPUPercent < 0 || policy.CPUPercent > 100 || policy.RAMWarningMB < 0 || policy.RAMLimitMB < 0 {
		return errors.New("CPU must be 0–100 percent and RAM values must be nonnegative")
	}
	if uint64(policy.RAMWarningMB) > ^uint64(0)/(1024*1024) ||
		uint64(policy.RAMLimitMB) > ^uint64(0)/(1024*1024) {
		return errors.New("RAM value is too large")
	}
	if policy.CPUPercent == 0 && policy.AffinityMask == 0 && policy.RAMWarningMB == 0 && policy.RAMLimitMB == 0 {
		return errors.New("select at least one background policy or RAM warning")
	}
	return nil
}

func (policy Policy) LimitsRequested() bool {
	return policy.CPUPercent > 0 || policy.AffinityMask != 0 || policy.RAMLimitMB > 0
}

type Action string

const (
	ApplyLimits   Action = "apply"
	RestoreLimits Action = "restore"
	NoChange      Action = "none"
)

func Decide(policy Policy, foreground, applied bool) Action {
	if foreground && applied {
		return RestoreLimits
	}
	if !foreground && !applied && policy.LimitsRequested() {
		return ApplyLimits
	}
	return NoChange
}
