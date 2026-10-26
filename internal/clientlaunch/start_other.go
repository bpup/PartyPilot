//go:build !windows

package clientlaunch

import (
	"errors"
	"time"
)

func Start(paths []string, uris map[int]string, timeout time.Duration) (Result, error) {
	return Result{}, errors.New("game client launch is available in the Windows build")
}
