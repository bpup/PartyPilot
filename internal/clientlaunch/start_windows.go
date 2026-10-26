//go:build windows

package clientlaunch

import (
	"errors"
	"fmt"
	"os/exec"
	"path/filepath"
	"time"

	"partypilot/internal/windowlayout"
)

func Start(paths []string, timeout time.Duration) (Result, error) {
	result := Result{Clients: make([]Client, 0, len(paths))}
	for index, path := range paths {
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
