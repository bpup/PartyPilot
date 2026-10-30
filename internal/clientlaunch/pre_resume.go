package clientlaunch

import (
	"fmt"

	"partypilot/internal/clientprocess"
)

func validatePreResumeClients(paths []string, uris map[int]string,
	callbacks map[int]func(int) error) error {
	for client, callback := range callbacks {
		if client < 1 || client > len(paths) || callback == nil {
			return fmt.Errorf("pre-resume input needs a callback for a selected game client")
		}
		if !clientprocess.IsStandaloneGameExecutable(paths[client-1]) || uris[client] != "" {
			return fmt.Errorf("client %d pre-resume input requires a standalone game executable without a store URI", client)
		}
	}
	return nil
}
