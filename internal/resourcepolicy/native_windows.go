//go:build windows

package resourcepolicy

import (
	"context"
	"errors"
	"fmt"
	"os"
	"syscall"
	"time"
	"unsafe"

	"partypilot/internal/clientprocess"
)

const Supported = true

const (
	waitTimeout   = 0x102
	jobCPUClass   = 15
	jobLimitClass = 9
)

type cpuRate struct {
	Flags uint32
	Rate  uint32
}

type jobBasicLimit struct {
	ProcessTime int64
	JobTime     int64
	Flags       uint32
	MinWorking  uintptr
	MaxWorking  uintptr
	ActiveCount uint32
	Affinity    uintptr
	Priority    uint32
	Scheduling  uint32
}

type ioCounters struct {
	ReadOps, WriteOps, OtherOps       uint64
	ReadBytes, WriteBytes, OtherBytes uint64
}

type jobExtendedLimit struct {
	Basic                                                      jobBasicLimit
	IO                                                         ioCounters
	ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory uintptr
}

type processMemoryCounters struct {
	Size, PageFaults                                                                       uint32
	PeakWorking, Working, PeakPaged, Paged, PeakNonPaged, NonPaged, Pagefile, PeakPagefile uintptr
}

type target struct {
	pid               int
	process           syscall.Handle
	created           uint64
	job               uintptr
	jobReady          bool
	originalAffinity  uintptr
	affinitySet       bool
	applied           bool
	warned            bool
	lastRAMCheck      time.Time
	nextAttempt       time.Time
	lastLivenessError time.Time
	exited            bool
}

var (
	kernel32        = syscall.NewLazyDLL("kernel32.dll")
	user32          = syscall.NewLazyDLL("user32.dll")
	psapi           = syscall.NewLazyDLL("psapi.dll")
	procCreateJob   = kernel32.NewProc("CreateJobObjectW")
	procSetJobInfo  = kernel32.NewProc("SetInformationJobObject")
	procAssignJob   = kernel32.NewProc("AssignProcessToJobObject")
	procIsInJob     = kernel32.NewProc("IsProcessInJob")
	procGetAffinity = kernel32.NewProc("GetProcessAffinityMask")
	procSetAffinity = kernel32.NewProc("SetProcessAffinityMask")
	procCloseHandle = kernel32.NewProc("CloseHandle")
	procForeground  = user32.NewProc("GetForegroundWindow")
	procWindowPID   = user32.NewProc("GetWindowThreadProcessId")
	procGetMemory   = psapi.NewProc("GetProcessMemoryInfo")
)

func Run(ctx context.Context, pids []int, policy Policy) (runErr error) {
	if err := policy.Validate(); err != nil {
		return err
	}
	if len(pids) < 1 || len(pids) > 3 {
		return fmt.Errorf("select 1–3 game process IDs")
	}
	seen := map[int]bool{}
	targets := make([]*target, 0, len(pids))
	defer func() {
		for _, current := range targets {
			if !current.exited {
				if err := current.restore(); err != nil {
					runErr = errors.Join(runErr, fmt.Errorf("process %d restoration: %w", current.pid, err))
				}
			}
			current.close()
		}
	}()
	for _, pid := range pids {
		if pid <= 0 || seen[pid] {
			return fmt.Errorf("invalid or duplicate process ID %d", pid)
		}
		seen[pid] = true
		image, err := clientprocess.ExecutablePath(pid)
		if err != nil {
			return fmt.Errorf("process %d: %w", pid, err)
		}
		if !clientprocess.IsKnownGameExecutable(image) {
			return fmt.Errorf("process %d is not a known game executable", pid)
		}
		rights := uint32(0x101000) // SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION
		if policy.AffinityMask != 0 {
			rights |= 0x200 // PROCESS_SET_INFORMATION
		}
		if policy.CPUPercent > 0 || policy.RAMLimitMB > 0 {
			rights |= 0x101 // PROCESS_SET_QUOTA | PROCESS_TERMINATE
		}
		handle, err := syscall.OpenProcess(rights, false, uint32(pid))
		if err != nil {
			return fmt.Errorf("process %d cannot be opened for reversible limits: %w", pid, err)
		}
		current := &target{pid: pid, process: handle}
		targets = append(targets, current)
		var creation, exit, kernel, user syscall.Filetime
		if err := syscall.GetProcessTimes(handle, &creation, &exit, &kernel, &user); err != nil {
			return fmt.Errorf("process %d creation time: %w", pid, err)
		}
		current.created = uint64(creation.HighDateTime)<<32 | uint64(creation.LowDateTime)
		alive, err := current.alive()
		if err != nil {
			return fmt.Errorf("process %d liveness: %w", pid, err)
		}
		if !alive {
			return fmt.Errorf("process %d exited during validation", pid)
		}
	}
	ticker := time.NewTicker(500 * time.Millisecond)
	defer ticker.Stop()
	for {
		active := 0
		foreground := foregroundPID()
		now := time.Now()
		for _, current := range targets {
			if current.exited {
				continue
			}
			alive, err := current.alive()
			if err != nil {
				active++
				if now.Sub(current.lastLivenessError) >= 5*time.Second {
					fmt.Fprintf(os.Stderr, "process %d liveness unavailable: %v\n", current.pid, err)
					current.lastLivenessError = now
				}
				continue
			}
			if !alive {
				current.exited = true
				if err := current.restore(); err != nil {
					runErr = errors.Join(runErr, fmt.Errorf("process %d exit cleanup: %w", current.pid, err))
				}
				fmt.Fprintf(os.Stderr, "process %d exited; resource policy released\n", current.pid)
				continue
			}
			active++
			if current.applied && !current.nextAttempt.IsZero() && !now.Before(current.nextAttempt) {
				if err := current.restore(); err != nil {
					current.nextAttempt = now.Add(5 * time.Second)
					fmt.Fprintf(os.Stderr, "process %d partial limit cleanup needs retry: %v\n", current.pid, err)
					continue
				}
				current.nextAttempt = time.Time{}
			}
			// Treat an unknown foreground as active so no new hard limit is applied.
			switch Decide(policy, foreground == 0 || current.pid == foreground, current.applied) {
			case ApplyLimits:
				if !now.Before(current.nextAttempt) {
					if err := current.apply(policy); err != nil {
						current.nextAttempt = now.Add(5 * time.Second)
						fmt.Fprintf(os.Stderr, "process %d background limits unavailable: %v\n", current.pid, err)
					} else {
						fmt.Fprintf(os.Stderr, "process %d background limits applied\n", current.pid)
					}
				}
			case RestoreLimits:
				if err := current.restore(); err != nil {
					fmt.Fprintf(os.Stderr, "process %d foreground restoration needs retry: %v\n", current.pid, err)
				} else {
					fmt.Fprintf(os.Stderr, "process %d foreground resources restored\n", current.pid)
				}
			}
			if policy.RAMWarningMB > 0 && now.Sub(current.lastRAMCheck) >= 5*time.Second {
				current.lastRAMCheck = now
				current.checkRAM(policy.RAMWarningMB)
			}
		}
		if active == 0 {
			return runErr
		}
		select {
		case <-ctx.Done():
			return runErr
		case <-ticker.C:
		}
	}
}

func (current *target) alive() (bool, error) {
	status, err := syscall.WaitForSingleObject(current.process, 0)
	if err != nil {
		return false, err
	}
	switch status {
	case waitTimeout:
		return true, nil
	case 0:
		return false, nil
	default:
		return false, fmt.Errorf("unexpected wait status 0x%x", status)
	}
}

func (current *target) close() {
	if current.job != 0 {
		procCloseHandle.Call(current.job)
		current.job = 0
	}
	if current.process != 0 {
		syscall.CloseHandle(current.process)
		current.process = 0
	}
}

func foregroundPID() int {
	handle, _, _ := procForeground.Call()
	if handle == 0 {
		return 0
	}
	var pid uint32
	procWindowPID.Call(handle, uintptr(unsafe.Pointer(&pid)))
	return int(pid)
}

func (current *target) apply(policy Policy) (err error) {
	current.applied = true // Retain a restore attempt after any partial native change.
	defer func() {
		if err != nil {
			_ = current.restore()
		}
	}()
	if policy.CPUPercent > 0 || policy.RAMLimitMB > 0 {
		if err = current.ensureJob(); err != nil {
			return err
		}
		if err = setJobMemory(current.job, policy.RAMLimitMB); err != nil {
			return err
		}
		if err = setJobCPU(current.job, policy.CPUPercent); err != nil {
			return err
		}
	}
	if policy.AffinityMask != 0 {
		var previous, system uintptr
		result, _, callErr := procGetAffinity.Call(uintptr(current.process), uintptr(unsafe.Pointer(&previous)), uintptr(unsafe.Pointer(&system)))
		if result == 0 {
			return fmt.Errorf("GetProcessAffinityMask: %w", callErr)
		}
		requested := uintptr(policy.AffinityMask)
		if uint64(requested) != policy.AffinityMask || requested&system != requested {
			return fmt.Errorf("requested affinity includes unavailable processors")
		}
		current.originalAffinity = previous
		result, _, callErr = procSetAffinity.Call(uintptr(current.process), requested)
		if result == 0 {
			return fmt.Errorf("SetProcessAffinityMask: %w", callErr)
		}
		current.affinitySet = true
	}
	return nil
}

func (current *target) restore() error {
	var first error
	if current.job != 0 {
		if err := setJobCPU(current.job, 0); err != nil {
			first = err
		}
		if err := setJobMemory(current.job, 0); err != nil && first == nil {
			first = err
		}
	}
	if current.affinitySet {
		alive, err := current.alive()
		if err != nil {
			if first == nil {
				first = fmt.Errorf("check process before affinity restoration: %w", err)
			}
		} else if !alive {
			current.affinitySet = false
		} else {
			result, _, callErr := procSetAffinity.Call(uintptr(current.process), current.originalAffinity)
			if result == 0 {
				if first == nil {
					first = fmt.Errorf("restore process affinity: %w", callErr)
				}
			} else {
				current.affinitySet = false
			}
		}
	}
	if first == nil {
		current.applied = false
	}
	return first
}

func (current *target) ensureJob() error {
	if current.jobReady {
		return nil
	}
	if current.job == 0 {
		name, _ := syscall.UTF16PtrFromString(fmt.Sprintf("Local\\PartyPilot.Resource.%d.%d", current.pid, current.created))
		job, _, err := procCreateJob.Call(0, uintptr(unsafe.Pointer(name)))
		if job == 0 {
			return fmt.Errorf("CreateJobObjectW: %w", err)
		}
		current.job = job
	}
	job := current.job
	// A prior crash can leave this named job attached to a live process.
	if err := setJobCPU(job, 0); err != nil {
		return err
	}
	if err := setJobMemory(job, 0); err != nil {
		return err
	}
	var member uint32
	result, _, err := procIsInJob.Call(uintptr(current.process), job, uintptr(unsafe.Pointer(&member)))
	if result == 0 {
		return fmt.Errorf("IsProcessInJob: %w", err)
	}
	if member == 0 {
		result, _, err = procAssignJob.Call(job, uintptr(current.process))
		if result == 0 {
			return fmt.Errorf("AssignProcessToJobObject: %w", err)
		}
	}
	current.jobReady = true
	return nil
}

func setJobCPU(job uintptr, percent int) error {
	information := cpuRate{}
	if percent > 0 {
		information = cpuRate{Flags: 0x5, Rate: uint32(percent * 100)}
	}
	result, _, err := procSetJobInfo.Call(job, jobCPUClass, uintptr(unsafe.Pointer(&information)), unsafe.Sizeof(information))
	if result == 0 {
		return fmt.Errorf("set job CPU rate: %w", err)
	}
	return nil
}

func setJobMemory(job uintptr, megabytes int) error {
	information := jobExtendedLimit{}
	if megabytes > 0 {
		information.Basic.Flags = 0x100
		information.ProcessMemory = uintptr(megabytes) * 1024 * 1024
	}
	result, _, err := procSetJobInfo.Call(job, jobLimitClass, uintptr(unsafe.Pointer(&information)), unsafe.Sizeof(information))
	if result == 0 {
		return fmt.Errorf("set job memory limit: %w", err)
	}
	return nil
}

func (current *target) checkRAM(warningMB int) {
	counters := processMemoryCounters{Size: uint32(unsafe.Sizeof(processMemoryCounters{}))}
	result, _, err := procGetMemory.Call(uintptr(current.process), uintptr(unsafe.Pointer(&counters)), uintptr(counters.Size))
	if result == 0 {
		fmt.Fprintf(os.Stderr, "process %d RAM usage unavailable: %v\n", current.pid, err)
		return
	}
	warned := uint64(counters.Working) >= uint64(warningMB)*1024*1024
	if warned != current.warned {
		current.warned = warned
		fmt.Fprintf(os.Stderr, "process %d RAM warning %t: %d MB working set, threshold %d MB\n",
			current.pid, warned, uint64(counters.Working)/1024/1024, warningMB)
	}
}
