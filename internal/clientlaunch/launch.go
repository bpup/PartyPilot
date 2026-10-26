package clientlaunch

import (
	"fmt"
	"os"
	"path/filepath"
	"time"

	"partypilot/internal/clientprocess"
	"partypilot/internal/windowlayout"
)

type Client struct {
	Executable string              `json:"executable"`
	PID        int                 `json:"pid"`
	Window     windowlayout.Window `json:"window"`
}

type Result struct {
	Clients []Client `json:"clients"`
}

func Validate(paths []string, timeout time.Duration) ([]string, error) {
	if len(paths) < 1 || len(paths) > 3 || timeout <= 0 {
		return nil, fmt.Errorf("configure 1–3 executables and a positive timeout")
	}
	validated := make([]string, 0, len(paths))
	var roots []os.FileInfo
	for _, path := range paths {
		full, err := filepath.Abs(path)
		if err != nil {
			return nil, err
		}
		full, err = filepath.EvalSymlinks(full)
		if err != nil {
			return nil, err
		}
		if !clientprocess.IsKnownGameExecutable(full) {
			return nil, fmt.Errorf("unknown game executable: %s", path)
		}
		info, err := os.Stat(full)
		if err != nil || !info.Mode().IsRegular() {
			return nil, fmt.Errorf("game executable is not a regular file: %s", path)
		}
		root, err := os.Stat(filepath.Dir(full))
		if err != nil {
			return nil, err
		}
		for _, prior := range roots {
			if os.SameFile(root, prior) {
				return nil, fmt.Errorf("multiple clients share one installation directory; independent multi-instance launch is not implemented")
			}
		}
		roots = append(roots, root)
		validated = append(validated, full)
	}
	return validated, nil
}
