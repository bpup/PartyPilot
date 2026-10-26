//go:build windows

package windowlayout

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"syscall"
	"unsafe"
)

const (
	gwlStyle   = ^uintptr(15) // -16
	gwlExStyle = ^uintptr(19) // -20
)

var (
	procGetWindowLong  = user32.NewProc("GetWindowLongW")
	procSetWindowLong  = user32.NewProc("SetWindowLongW")
	procClearLastError = syscall.NewLazyDLL("kernel32.dll").NewProc("SetLastError")
	procMoveFileEx     = syscall.NewLazyDLL("kernel32.dll").NewProc("MoveFileExW")
)

func styleStatePath() (string, error) {
	directory, err := os.UserConfigDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(directory, "PartyPilot", "window-styles.json"), nil
}

func loadStyleState(path string) ([]savedStyle, error) {
	data, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	var entries []savedStyle
	if err := json.Unmarshal(data, &entries); err != nil {
		return nil, fmt.Errorf("invalid saved window styles: %w", err)
	}
	return entries, nil
}

func saveStyleState(path string, entries []savedStyle) error {
	if err := os.MkdirAll(filepath.Dir(path), 0700); err != nil {
		return err
	}
	data, err := json.MarshalIndent(entries, "", "  ")
	if err != nil {
		return err
	}
	tmp, err := os.CreateTemp(filepath.Dir(path), "window-styles-*.tmp")
	if err != nil {
		return err
	}
	defer os.Remove(tmp.Name())
	if _, err = tmp.Write(data); err != nil {
		tmp.Close()
		return err
	}
	if err = tmp.Close(); err != nil {
		return err
	}
	oldName, err := syscall.UTF16PtrFromString(tmp.Name())
	if err != nil {
		return err
	}
	newName, err := syscall.UTF16PtrFromString(path)
	if err != nil {
		return err
	}
	// Replace the previous snapshot only after the new file is complete.
	result, _, callErr := procMoveFileEx.Call(uintptr(unsafe.Pointer(oldName)), uintptr(unsafe.Pointer(newName)), 0x9)
	if result == 0 {
		return callErr
	}
	return nil
}

func processCreation(pid int) (uint64, error) {
	handle, err := syscall.OpenProcess(0x1000, false, uint32(pid))
	if err != nil {
		return 0, err
	}
	defer syscall.CloseHandle(handle)
	var creation, exit, kernel, user syscall.Filetime
	if err := syscall.GetProcessTimes(handle, &creation, &exit, &kernel, &user); err != nil {
		return 0, err
	}
	return uint64(creation.HighDateTime)<<32 | uint64(creation.LowDateTime), nil
}

func readWindowStyle(handle uintptr) (windowStyle, error) {
	style, err := getWindowLong(handle, gwlStyle)
	if err != nil {
		return windowStyle{}, err
	}
	exStyle, err := getWindowLong(handle, gwlExStyle)
	if err != nil {
		return windowStyle{}, err
	}
	return windowStyle{Style: style, ExStyle: exStyle}, nil
}

func getWindowLong(handle, index uintptr) (uint32, error) {
	procClearLastError.Call(0)
	value, _, err := procGetWindowLong.Call(handle, index)
	if value == 0 && err != syscall.Errno(0) {
		return 0, err
	}
	return uint32(value), nil
}

func setWindowLong(handle, index uintptr, value uint32) error {
	procClearLastError.Call(0)
	previous, _, err := procSetWindowLong.Call(handle, index, uintptr(value))
	if previous == 0 && err != syscall.Errno(0) {
		return err
	}
	return nil
}

func writeWindowStyle(handle uintptr, desired windowStyle) error {
	previous, err := readWindowStyle(handle)
	if err != nil {
		return err
	}
	if previous == desired {
		return nil
	}
	if err := setWindowLong(handle, gwlStyle, desired.Style); err != nil {
		return err
	}
	if err := setWindowLong(handle, gwlExStyle, desired.ExStyle); err != nil {
		_ = setWindowLong(handle, gwlStyle, previous.Style)
		return err
	}
	// Force Windows to recalculate the non-client frame before positioning.
	result, _, err := procSetWindowPos.Call(handle, 0, 0, 0, 0, 0, 0x37)
	if result == 0 {
		_ = setWindowLong(handle, gwlStyle, previous.Style)
		_ = setWindowLong(handle, gwlExStyle, previous.ExStyle)
		return err
	}
	return nil
}
