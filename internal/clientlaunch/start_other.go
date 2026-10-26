//go:build !windows

package clientlaunch

import (
	"errors"
	"time"
)

func Start(paths []string, uris map[int]string, timeout time.Duration) (Result, error) {
	return Result{}, errors.New("game client launch is available in the Windows build")
}

func Attach(paths []string, selected map[int]int) (Result, error) {
	return Result{}, errors.New("game client window attachment is available in the Windows build")
}
