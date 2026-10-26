//go:build windows

package clientlaunch

import (
	"errors"
	"fmt"
	"path/filepath"
	"syscall"
	"time"
	"unsafe"

	"partypilot/internal/windowlayout"
)

const (
	createSuspended  = 0x00000004
	memCommitReserve = 0x3000
	memRelease       = 0x8000
	pageReadWrite    = 0x04
	waitObject0      = 0
	waitTimeout      = 258
)

var (
	kernel32           = syscall.NewLazyDLL("kernel32.dll")
	createEventW       = kernel32.NewProc("CreateEventW")
	virtualAllocEx     = kernel32.NewProc("VirtualAllocEx")
	virtualFreeEx      = kernel32.NewProc("VirtualFreeEx")
	writeProcessMemory = kernel32.NewProc("WriteProcessMemory")
	createRemoteThread = kernel32.NewProc("CreateRemoteThread")
	resumeThread       = kernel32.NewProc("ResumeThread")
)

func startIsolatedStandalone(path, runtime string, timeout time.Duration) (Client, error) {
	application, err := syscall.UTF16PtrFromString(path)
	if err != nil {
		return Client{}, err
	}
	command, err := syscall.UTF16PtrFromString(`"` + path + `"`)
	if err != nil {
		return Client{}, err
	}
	directory, err := syscall.UTF16PtrFromString(filepath.Dir(path))
	if err != nil {
		return Client{}, err
	}
	startup := syscall.StartupInfo{Cb: uint32(unsafe.Sizeof(syscall.StartupInfo{}))}
	var process syscall.ProcessInformation
	if err := syscall.CreateProcess(application, command, nil, nil, false,
		createSuspended|syscall.CREATE_UNICODE_ENVIRONMENT, nil, directory, &startup, &process); err != nil {
		return Client{}, fmt.Errorf("create suspended standalone client: %w", err)
	}
	defer syscall.CloseHandle(process.Thread)
	defer syscall.CloseHandle(process.Process)
	resumed := false
	defer func() {
		if !resumed {
			_ = syscall.TerminateProcess(process.Process, 1)
		}
	}()

	pid := int(process.ProcessId)
	ready, err := newNamedEvent(fmt.Sprintf(`Local\PartyPilot.MultiInstance.Ready.%d`, pid))
	if err != nil {
		return Client{}, fmt.Errorf("create runtime ready event: %w", err)
	}
	defer syscall.CloseHandle(ready)
	failed, err := newNamedEvent(fmt.Sprintf(`Local\PartyPilot.MultiInstance.Failed.%d`, pid))
	if err != nil {
		return Client{}, fmt.Errorf("create runtime failure event: %w", err)
	}
	defer syscall.CloseHandle(failed)
	if err := injectMutexRuntime(process.Process, runtime); err != nil {
		return Client{}, err
	}
	deadline := time.Now().Add(15 * time.Second)
	for {
		result, waitErr := syscall.WaitForSingleObject(ready, 0)
		if waitErr != nil {
			return Client{}, fmt.Errorf("wait for mutex runtime: %w", waitErr)
		}
		if result == waitObject0 {
			break
		}
		result, waitErr = syscall.WaitForSingleObject(failed, 0)
		if waitErr != nil {
			return Client{}, fmt.Errorf("wait for mutex runtime failure: %w", waitErr)
		}
		if result == waitObject0 {
			return Client{}, errors.New("independent mutex runtime rejected its hooks")
		}
		if time.Now().After(deadline) {
			return Client{}, errors.New("independent mutex runtime did not become ready in 15 seconds")
		}
		time.Sleep(50 * time.Millisecond)
	}
	previous, _, callErr := resumeThread.Call(uintptr(process.Thread))
	if previous != 1 {
		return Client{}, fmt.Errorf("resume isolated standalone client (previous suspend count %d): %w", previous, callErr)
	}
	resumed = true
	client := Client{Executable: path, PID: pid}
	deadline = time.Now().Add(timeout)
	for {
		windows, discoverErr := windowlayout.DiscoverWindows([]int{pid})
		if discoverErr == nil && len(windows) == 1 {
			client.Window = windows[0]
			return client, nil
		}
		if discoverErr != nil && !errors.Is(discoverErr, windowlayout.ErrNoWindow) {
			return client, fmt.Errorf("isolated client window discovery: %w", discoverErr)
		}
		var exitCode uint32
		if err := syscall.GetExitCodeProcess(process.Process, &exitCode); err != nil || exitCode != 259 {
			return client, fmt.Errorf("isolated client exited before its window appeared (code %d): %v", exitCode, err)
		}
		if time.Now().After(deadline) {
			return client, fmt.Errorf("isolated client did not present a game window within %s", timeout)
		}
		time.Sleep(250 * time.Millisecond)
	}
}

func newNamedEvent(name string) (syscall.Handle, error) {
	wide, err := syscall.UTF16PtrFromString(name)
	if err != nil {
		return 0, err
	}
	handle, _, callErr := createEventW.Call(0, 0, 0, uintptr(unsafe.Pointer(wide)))
	if handle == 0 {
		return 0, callErr
	}
	return syscall.Handle(handle), nil
}

func injectMutexRuntime(process syscall.Handle, path string) error {
	wide, err := syscall.UTF16FromString(path)
	if err != nil {
		return err
	}
	size := uintptr(len(wide) * 2)
	remote, _, callErr := virtualAllocEx.Call(uintptr(process), 0, size, memCommitReserve, pageReadWrite)
	if remote == 0 {
		return fmt.Errorf("allocate mutex runtime path: %w", callErr)
	}
	loaderFinished := false
	defer func() {
		if loaderFinished {
			virtualFreeEx.Call(uintptr(process), remote, 0, memRelease)
		}
	}()
	var written uintptr
	ok, _, callErr := writeProcessMemory.Call(uintptr(process), remote,
		uintptr(unsafe.Pointer(&wide[0])), size, uintptr(unsafe.Pointer(&written)))
	if ok == 0 || written != size {
		return fmt.Errorf("write mutex runtime path: %w", callErr)
	}
	module, err := syscall.LoadDLL("kernel32.dll")
	if err != nil {
		return err
	}
	defer module.Release()
	loader, err := module.FindProc("LoadLibraryW")
	if err != nil {
		return err
	}
	var threadID uint32
	thread, _, callErr := createRemoteThread.Call(uintptr(process), 0, 0,
		loader.Addr(), remote, 0, uintptr(unsafe.Pointer(&threadID)))
	if thread == 0 {
		return fmt.Errorf("load independent mutex runtime: %w", callErr)
	}
	defer syscall.CloseHandle(syscall.Handle(thread))
	result, err := syscall.WaitForSingleObject(syscall.Handle(thread), 15000)
	if err != nil {
		return fmt.Errorf("wait for runtime loader: %w", err)
	}
	if result == waitTimeout {
		return errors.New("runtime loader did not finish in 15 seconds")
	}
	if result != waitObject0 {
		return fmt.Errorf("runtime loader returned wait status %d", result)
	}
	loaderFinished = true
	return nil
}
