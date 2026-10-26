package clientlaunch

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

// SelectAttachedProcess requires one process with the exact configured executable identity.
// A second matching process is ambiguous even if only one currently has a visible window.
func SelectAttachedProcess(expected string, processes map[int]string) (int, error) {
	match := 0
	for pid, actual := range processes {
		if pid <= 0 || !sameExecutable(actual, expected) {
			continue
		}
		if match != 0 {
			return 0, fmt.Errorf("multiple running processes use %s; select a unique installation", filepath.Base(expected))
		}
		match = pid
	}
	if match == 0 {
		return 0, fmt.Errorf("no running process uses %s", expected)
	}
	return match, nil
}

func sameExecutable(actual, expected string) bool {
	if strings.EqualFold(filepath.Clean(actual), filepath.Clean(expected)) {
		return true
	}
	left, leftErr := os.Stat(actual)
	right, rightErr := os.Stat(expected)
	return leftErr == nil && rightErr == nil && os.SameFile(left, right)
}
