//go:build !windows

package clientprocess

import "errors"

const Supported = false

func ExecutablePath(pid int) (string, error) {
	return "", errors.New("process executable lookup is available in the Windows build")
}
