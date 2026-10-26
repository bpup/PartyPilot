package clientprocess

import (
	"fmt"
	"os"
	"path/filepath"
)

// ValidateLogBinding checks the same per-installation layout used by the
// supplied build: the game image and its logs/Client.txt share one root.
func ValidateLogBinding(executablePath, configuredLog string) error {
	if executablePath == "" || configuredLog == "" {
		return fmt.Errorf("empty game executable or log path")
	}
	expected := filepath.Join(filepath.Dir(executablePath), "logs", "Client.txt")
	expectedInfo, err := os.Stat(expected)
	if err != nil || !expectedInfo.Mode().IsRegular() {
		return fmt.Errorf("process log is not a regular file: %s", expected)
	}
	configuredInfo, err := os.Stat(configuredLog)
	if err != nil || !configuredInfo.Mode().IsRegular() {
		return fmt.Errorf("configured log is not a regular file: %s", configuredLog)
	}
	if !os.SameFile(expectedInfo, configuredInfo) {
		return fmt.Errorf("configured log does not belong to process executable")
	}
	return nil
}
