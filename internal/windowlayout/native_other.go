//go:build !windows

package windowlayout

import "errors"

const Supported = false

type Window struct {
	PID    int     `json:"pid"`
	Handle uintptr `json:"handle"`
	Bounds Rect    `json:"bounds"`
}

func DiscoverDisplays() ([]Display, error) {
	return nil, errors.New("display discovery is available in the Windows build")
}

func DiscoverWindows(pids []int) ([]Window, error) {
	return nil, errors.New("game window discovery is available in the Windows build")
}

func Apply(windows []Window, plan Plan) error {
	return errors.New("window placement is available in the Windows build")
}
