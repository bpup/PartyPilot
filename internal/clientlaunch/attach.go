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

// SelectAttachedPID verifies the selected process rather than silently choosing
// between several clients from the same installation.
func SelectAttachedPID(expected string, selected int, processes map[int]string) (int, error) {
	if selected <= 0 || !sameExecutable(processes[selected], expected) {
		return 0, fmt.Errorf("process %d does not use %s", selected, expected)
	}
	return selected, nil
}

func ValidateAttachPIDs(paths []string, selected map[int]int) error {
	used := make(map[int]bool)
	for slot, pid := range selected {
		if slot < 1 || slot > len(paths) || pid <= 0 || used[pid] {
			return fmt.Errorf("invalid or duplicate process ID for client %d", slot)
		}
		used[pid] = true
	}
	for index, path := range paths {
		for prior := 0; prior < index; prior++ {
			if sameExecutable(path, paths[prior]) && (selected[index+1] == 0 || selected[prior+1] == 0) {
				return fmt.Errorf("clients %d and %d share one installation; specify a distinct -pid for each", prior+1, index+1)
			}
		}
	}
	return nil
}

func sameExecutable(actual, expected string) bool {
	if strings.EqualFold(filepath.Clean(actual), filepath.Clean(expected)) {
		return true
	}
	left, leftErr := os.Stat(actual)
	right, rightErr := os.Stat(expected)
	return leftErr == nil && rightErr == nil && os.SameFile(left, right)
}
