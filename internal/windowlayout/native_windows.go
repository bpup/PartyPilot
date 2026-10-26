//go:build windows

package windowlayout

import (
	"fmt"
	"sort"
	"sync"
	"syscall"
	"time"
	"unsafe"

	"partypilot/internal/clientprocess"
)

const Supported = true

type nativeRect struct{ Left, Top, Right, Bottom int32 }
type monitorInfo struct {
	Size    uint32
	Monitor nativeRect
	Work    nativeRect
	Flags   uint32
}

type Window struct {
	PID    int     `json:"pid"`
	Handle uintptr `json:"handle"`
	Bounds Rect    `json:"bounds"`
}

var (
	user32                  = syscall.NewLazyDLL("user32.dll")
	procSetDPI              = user32.NewProc("SetProcessDpiAwarenessContext")
	procEnumDisplayMonitors = user32.NewProc("EnumDisplayMonitors")
	procGetMonitorInfo      = user32.NewProc("GetMonitorInfoW")
	procEnumWindows         = user32.NewProc("EnumWindows")
	procGetWindowPID        = user32.NewProc("GetWindowThreadProcessId")
	procIsWindowVisible     = user32.NewProc("IsWindowVisible")
	procGetWindowRect       = user32.NewProc("GetWindowRect")
	procShowWindowAsync     = user32.NewProc("ShowWindowAsync")
	procSetWindowPos        = user32.NewProc("SetWindowPos")
	dpiOnce                 sync.Once
	dpiError                error
)

func ensureDPI() error {
	dpiOnce.Do(func() {
		// DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 is the signed handle -4.
		result, _, err := procSetDPI.Call(^uintptr(3))
		if result == 0 {
			dpiError = fmt.Errorf("cannot enable per-monitor DPI awareness: %w", err)
		}
	})
	return dpiError
}

func DiscoverDisplays() ([]Display, error) {
	if err := ensureDPI(); err != nil {
		return nil, err
	}
	var found []Display
	var callbackError error
	callback := syscall.NewCallback(func(monitor, _, _, _ uintptr) uintptr {
		info := monitorInfo{Size: uint32(unsafe.Sizeof(monitorInfo{}))}
		result, _, err := procGetMonitorInfo.Call(monitor, uintptr(unsafe.Pointer(&info)))
		if result == 0 {
			callbackError = fmt.Errorf("GetMonitorInfoW: %w", err)
			return 0
		}
		work := nativeToRect(info.Work)
		if work.Width >= 320 && work.Height >= 240 {
			found = append(found, Display{Work: work, Primary: info.Flags&1 != 0})
		}
		return 1
	})
	result, _, err := procEnumDisplayMonitors.Call(0, 0, callback, 0)
	if callbackError != nil {
		return nil, callbackError
	}
	if result == 0 {
		return nil, fmt.Errorf("EnumDisplayMonitors: %w", err)
	}
	if len(found) == 0 {
		return nil, fmt.Errorf("no usable displays found")
	}
	sort.Slice(found, func(i, j int) bool {
		if found[i].Primary != found[j].Primary {
			return found[i].Primary
		}
		if found[i].Work.Left != found[j].Work.Left {
			return found[i].Work.Left < found[j].Work.Left
		}
		return found[i].Work.Top < found[j].Work.Top
	})
	for index := range found {
		found[index].ID = fmt.Sprintf("monitor-%d", index+1)
	}
	return found, nil
}

func DiscoverWindows(pids []int) ([]Window, error) {
	if err := ensureDPI(); err != nil {
		return nil, err
	}
	if len(pids) < 1 || len(pids) > 3 {
		return nil, fmt.Errorf("select 1–3 client process IDs")
	}
	requested := make(map[int]bool, len(pids))
	for _, pid := range pids {
		if pid <= 0 || requested[pid] {
			return nil, fmt.Errorf("invalid or duplicate process ID")
		}
		image, err := clientprocess.ExecutablePath(pid)
		if err != nil {
			return nil, fmt.Errorf("process %d: %w", pid, err)
		}
		if !clientprocess.IsKnownGameExecutable(image) {
			return nil, fmt.Errorf("process %d is not a known game executable", pid)
		}
		requested[pid] = true
	}
	best := make(map[int]Window, len(pids))
	callback := syscall.NewCallback(func(handle, _ uintptr) uintptr {
		var processID uint32
		thread, _, _ := procGetWindowPID.Call(handle, uintptr(unsafe.Pointer(&processID)))
		if thread == 0 || !requested[int(processID)] {
			return 1
		}
		visible, _, _ := procIsWindowVisible.Call(handle)
		if visible == 0 {
			return 1
		}
		bounds, err := windowRect(handle)
		if err != nil || bounds.Width < 320 || bounds.Height < 240 {
			return 1
		}
		previous := best[int(processID)]
		if bounds.Width*bounds.Height > previous.Bounds.Width*previous.Bounds.Height {
			best[int(processID)] = Window{PID: int(processID), Handle: handle, Bounds: bounds}
		}
		return 1
	})
	result, _, err := procEnumWindows.Call(callback, 0)
	if result == 0 {
		return nil, fmt.Errorf("EnumWindows: %w", err)
	}
	windows := make([]Window, 0, len(pids))
	for _, pid := range pids {
		window, exists := best[pid]
		if !exists {
			return nil, fmt.Errorf("%w for process %d", ErrNoWindow, pid)
		}
		windows = append(windows, window)
	}
	return windows, nil
}

func Apply(windows []Window, plan Plan) error {
	if plan.Borderless {
		return fmt.Errorf("full borderless style is not implemented")
	}
	if len(windows) < 1 || len(windows) > 3 || len(windows) != len(plan.Placements) {
		return fmt.Errorf("window count does not match plan")
	}
	for index, window := range windows {
		var pid uint32
		thread, _, _ := procGetWindowPID.Call(window.Handle, uintptr(unsafe.Pointer(&pid)))
		if thread == 0 || int(pid) != window.PID {
			return fmt.Errorf("client %d window identity changed", index+1)
		}
		placement := plan.Placements[index]
		if placement.Width < 320 || placement.Height < 240 {
			return fmt.Errorf("client %d placement is below game window minimum", index+1)
		}
	}
	for index, window := range windows {
		placement := plan.Placements[index]
		procShowWindowAsync.Call(window.Handle, 9)
		// Keep Z order and focus. Apply asynchronously across process queues.
		result, _, err := procSetWindowPos.Call(window.Handle, 0,
			uintptr(int64(placement.Left)), uintptr(int64(placement.Top)),
			uintptr(placement.Width), uintptr(placement.Height), 0x4054)
		if result == 0 {
			return fmt.Errorf("client %d SetWindowPos: %w", index+1, err)
		}
	}
	for attempt := 0; attempt < 10; attempt++ {
		matched := true
		for index, window := range windows {
			actual, err := windowRect(window.Handle)
			if err != nil || !near(actual, plan.Placements[index], 4) {
				matched = false
				break
			}
		}
		if matched {
			return nil
		}
		time.Sleep(100 * time.Millisecond)
	}
	return fmt.Errorf("windows did not settle at requested positions")
}

func windowRect(handle uintptr) (Rect, error) {
	var native nativeRect
	result, _, err := procGetWindowRect.Call(handle, uintptr(unsafe.Pointer(&native)))
	if result == 0 {
		return Rect{}, err
	}
	return nativeToRect(native), nil
}

func nativeToRect(native nativeRect) Rect {
	return Rect{Left: int(native.Left), Top: int(native.Top), Width: int(native.Right - native.Left), Height: int(native.Bottom - native.Top)}
}

func near(a, b Rect, tolerance int) bool {
	return abs(a.Left-b.Left) <= tolerance && abs(a.Top-b.Top) <= tolerance &&
		abs(a.Width-b.Width) <= tolerance && abs(a.Height-b.Height) <= tolerance
}

func abs(value int) int {
	if value < 0 {
		return -value
	}
	return value
}
