//go:build windows

package clientlaunch

import (
	"errors"
	"fmt"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"
	"unsafe"

	"partypilot/internal/clientprocess"
	"partypilot/internal/windowlayout"
)

var shellExecute = syscall.NewLazyDLL("shell32.dll").NewProc("ShellExecuteW")

func Attach(paths []string, selected map[int]int) (Result, error) {
	if len(paths) < 1 || len(paths) > 3 {
		return Result{}, fmt.Errorf("select 1–3 configured game executables")
	}
	if err := ValidateAttachPIDs(paths, selected); err != nil {
		return Result{}, err
	}
	result := Result{Clients: make([]Client, 0, len(paths))}
	usedPIDs := make(map[int]bool)
	for index, path := range paths {
		ids, err := gameProcessIDs(filepath.Base(path))
		if err != nil {
			return Result{}, fmt.Errorf("client %d process snapshot: %w", index+1, err)
		}
		processes := make(map[int]string, len(ids))
		for pid := range ids {
			if selected[index+1] != 0 && pid != selected[index+1] {
				continue
			}
			actual, readErr := clientprocess.ExecutablePath(pid)
			if readErr != nil {
				return Result{}, fmt.Errorf("client %d cannot verify same-name process %d: %w", index+1, pid, readErr)
			}
			processes[pid] = actual
		}
		var pid int
		if selected[index+1] != 0 {
			pid, err = SelectAttachedPID(path, selected[index+1], processes)
		} else {
			pid, err = SelectAttachedProcess(path, processes)
		}
		if err != nil {
			return Result{}, fmt.Errorf("client %d: %w", index+1, err)
		}
		if usedPIDs[pid] {
			return Result{}, fmt.Errorf("client %d resolves to a process already bound to another client", index+1)
		}
		usedPIDs[pid] = true
		windows, err := windowlayout.DiscoverWindows([]int{pid})
		if err != nil {
			return Result{}, fmt.Errorf("client %d process %d has no unique usable game window: %w", index+1, pid, err)
		}
		if len(windows) != 1 || windows[0].PID != pid || windows[0].Handle == 0 {
			return Result{}, fmt.Errorf("client %d process %d has no unique usable game window", index+1, pid)
		}
		result.Clients = append(result.Clients, Client{Executable: path, PID: pid, Window: windows[0]})
	}
	return result, nil
}

func Start(paths []string, uris map[int]string, timeout time.Duration) (Result, error) {
	if err := ValidateURIs(paths, uris); err != nil {
		return Result{}, err
	}
	result := Result{Clients: make([]Client, 0, len(paths))}
	for index, path := range paths {
		if uri := uris[index+1]; uri != "" {
			client, err := startStoreClient(path, uri, timeout)
			if client.PID != 0 {
				result.Clients = append(result.Clients, client)
			}
			if err != nil {
				return result, fmt.Errorf("client %d store launch: %w", index+1, err)
			}
			continue
		}
		command := exec.Command(path)
		command.Dir = filepath.Dir(path)
		if err := command.Start(); err != nil {
			return result, fmt.Errorf("client %d launch: %w", index+1, err)
		}
		pid := command.Process.Pid
		client := Client{Executable: path, PID: pid}
		result.Clients = append(result.Clients, client)
		exited := make(chan error, 1)
		go func() { exited <- command.Wait() }()
		deadline := time.Now().Add(timeout)
		for {
			windows, err := windowlayout.DiscoverWindows([]int{pid})
			if err == nil && len(windows) == 1 {
				result.Clients[index].Window = windows[0]
				break
			}
			if err != nil && !errors.Is(err, windowlayout.ErrNoWindow) {
				return result, fmt.Errorf("client %d window discovery: %w", index+1, err)
			}
			select {
			case exitErr := <-exited:
				return result, fmt.Errorf("client %d exited before its window appeared: %v", index+1, exitErr)
			default:
			}
			if time.Now().After(deadline) {
				return result, fmt.Errorf("client %d did not present a game window within %s", index+1, timeout)
			}
			time.Sleep(250 * time.Millisecond)
		}
	}
	return result, nil
}

func startStoreClient(path, uri string, timeout time.Duration) (Client, error) {
	before, err := gameProcessIDs(filepath.Base(path))
	if err != nil {
		return Client{}, fmt.Errorf("process snapshot before launch: %w", err)
	}
	uriText, err := syscall.UTF16PtrFromString(uri)
	if err != nil {
		return Client{}, err
	}
	verb, _ := syscall.UTF16PtrFromString("open")
	result, _, callErr := shellExecute.Call(0, uintptr(unsafe.Pointer(verb)), uintptr(unsafe.Pointer(uriText)), 0, 0, 1)
	if result <= 32 {
		return Client{}, fmt.Errorf("ShellExecuteW rejected launch URI (code %d): %w", result, callErr)
	}
	deadline := time.Now().Add(timeout)
	for {
		ids, err := gameProcessIDs(filepath.Base(path))
		if err != nil {
			return Client{}, fmt.Errorf("process snapshot after launch: %w", err)
		}
		var candidates []int
		for pid := range ids {
			if before[pid] {
				continue
			}
			actual, err := clientprocess.ExecutablePath(pid)
			if err != nil || !sameExecutable(actual, path) {
				continue
			}
			candidates = append(candidates, pid)
		}
		if len(candidates) > 1 {
			return Client{}, fmt.Errorf("multiple new matching game processes appeared; cannot bind this URI to one window")
		}
		if len(candidates) == 1 {
			pid := candidates[0]
			client := Client{Executable: path, LaunchURI: uri, PID: pid}
			windows, err := windowlayout.DiscoverWindows([]int{pid})
			if err == nil && len(windows) == 1 {
				client.Window = windows[0]
				return client, nil
			}
			if err != nil && !errors.Is(err, windowlayout.ErrNoWindow) {
				return client, fmt.Errorf("window discovery: %w", err)
			}
		}
		if time.Now().After(deadline) {
			return Client{}, fmt.Errorf("no unique new game window from %s within %s", filepath.Base(path), timeout)
		}
		time.Sleep(250 * time.Millisecond)
	}
}

func gameProcessIDs(name string) (map[int]bool, error) {
	snapshot, err := syscall.CreateToolhelp32Snapshot(syscall.TH32CS_SNAPPROCESS, 0)
	if err != nil {
		return nil, err
	}
	defer syscall.CloseHandle(snapshot)
	found := make(map[int]bool)
	entry := syscall.ProcessEntry32{Size: uint32(unsafe.Sizeof(syscall.ProcessEntry32{}))}
	if err := syscall.Process32First(snapshot, &entry); err != nil {
		if errors.Is(err, syscall.ERROR_NO_MORE_FILES) {
			return found, nil
		}
		return nil, err
	}
	for {
		if strings.EqualFold(syscall.UTF16ToString(entry.ExeFile[:]), name) {
			found[int(entry.ProcessID)] = true
		}
		err := syscall.Process32Next(snapshot, &entry)
		if errors.Is(err, syscall.ERROR_NO_MORE_FILES) {
			return found, nil
		}
		if err != nil {
			return nil, err
		}
	}
}
