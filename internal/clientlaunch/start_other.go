//go:build !windows

package clientlaunch

import (
	"errors"
	"time"
)

func Start(paths []string, timeout time.Duration) (Result, error) {
	return Result{}, errors.New("game client launch is available in the Windows build")
}
