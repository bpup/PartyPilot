//go:build !windows

package clientlaunch

import (
	"errors"
	"time"
)

func Start(paths []string, uris map[int]string, timeout time.Duration, runtime string) (Result, error) {
	return Result{}, errors.New("game client launch is available in the Windows build")
}

func StartWithPreResume(paths []string, uris map[int]string, timeout time.Duration,
	runtime string, beforeClient3Resume func(int) error) (Result, error) {
	return Result{}, errors.New("game client launch is available in the Windows build")
}

func StartWithPerClientPreResume(paths []string, uris map[int]string, timeout time.Duration,
	runtime string, beforeResumeByClient map[int]func(int) error) (Result, error) {
	return Result{}, errors.New("game client launch is available in the Windows build")
}

func Attach(paths []string, selected map[int]int) (Result, error) {
	return Result{}, errors.New("game client window attachment is available in the Windows build")
}
