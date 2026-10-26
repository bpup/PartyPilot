//go:build windows

package clientprocess

import (
	"fmt"
	"syscall"
	"unsafe"
)

const Supported = true

const processQueryLimitedInformation = 0x1000

var queryFullProcessImageName = syscall.NewLazyDLL("kernel32.dll").NewProc("QueryFullProcessImageNameW")

func ExecutablePath(pid int) (string, error) {
	if pid <= 0 {
		return "", fmt.Errorf("invalid process ID")
	}
	handle, err := syscall.OpenProcess(processQueryLimitedInformation, false, uint32(pid))
	if err != nil {
		return "", err
	}
	defer syscall.CloseHandle(handle)
	for capacity := 512; capacity <= 32768; capacity *= 2 {
		buffer := make([]uint16, capacity)
		size := uint32(len(buffer))
		result, _, callErr := queryFullProcessImageName.Call(
			uintptr(handle), 0, uintptr(unsafe.Pointer(&buffer[0])), uintptr(unsafe.Pointer(&size)),
		)
		if result != 0 && size > 0 && int(size) < len(buffer) {
			return syscall.UTF16ToString(buffer[:size]), nil
		}
		if result == 0 && callErr == syscall.ERROR_INSUFFICIENT_BUFFER {
			continue
		}
		if callErr != nil && callErr != syscall.Errno(0) {
			return "", callErr
		}
		return "", fmt.Errorf("could not read process executable path")
	}
	return "", fmt.Errorf("process executable path exceeds 32768 UTF-16 code units")
}
