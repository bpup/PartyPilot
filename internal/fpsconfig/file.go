package fpsconfig

import (
	"bytes"
	"errors"
	"fmt"
	"os"
	"path/filepath"
)

type Result struct {
	Path        string   `json:"path"`
	ChangedKeys []string `json:"changed_keys"`
	Applied     bool     `json:"applied"`
	Backup      string   `json:"backup,omitempty"`
}

// Prepare previews by default. Apply writes an exact backup of the file read
// and replaces the original only if it has not changed in the meantime.
func Prepare(path string, policy Policy, apply bool) (Result, error) {
	if path == "" {
		return Result{}, errors.New("an explicit config path is required")
	}
	info, err := os.Lstat(path)
	if err != nil {
		return Result{}, err
	}
	if !info.Mode().IsRegular() {
		return Result{}, errors.New("config must be a regular file")
	}
	before, err := os.ReadFile(path)
	if err != nil {
		return Result{}, err
	}
	after, keys, err := Patch(before, policy)
	if err != nil {
		return Result{}, err
	}
	result := Result{Path: path, ChangedKeys: keys}
	if !apply || len(keys) == 0 {
		return result, nil
	}
	directory := filepath.Dir(path)
	backup, err := os.CreateTemp(directory, filepath.Base(path)+".partypilot-*.bak")
	if err != nil {
		return Result{}, err
	}
	result.Backup = backup.Name()
	backupReady := false
	defer func() {
		if !backupReady {
			_ = os.Remove(backup.Name())
		}
	}()
	if err := backup.Chmod(info.Mode().Perm()); err != nil {
		_ = backup.Close()
		return Result{}, err
	}
	if _, err := backup.Write(before); err != nil {
		_ = backup.Close()
		return Result{}, err
	}
	if err := backup.Sync(); err != nil {
		_ = backup.Close()
		return Result{}, err
	}
	if err := backup.Close(); err != nil {
		return Result{}, err
	}
	backupReady = true
	current, err := os.ReadFile(path)
	if err != nil {
		return result, err
	}
	if !bytes.Equal(before, current) {
		return result, fmt.Errorf("config changed since inspection; original backup retained at %s", backup.Name())
	}
	temporary, err := os.CreateTemp(directory, filepath.Base(path)+".partypilot-*.tmp")
	if err != nil {
		return result, err
	}
	defer os.Remove(temporary.Name())
	if err := temporary.Chmod(info.Mode().Perm()); err != nil {
		_ = temporary.Close()
		return result, err
	}
	if _, err := temporary.Write(after); err != nil {
		_ = temporary.Close()
		return result, err
	}
	if err := temporary.Sync(); err != nil {
		_ = temporary.Close()
		return result, err
	}
	if err := temporary.Close(); err != nil {
		return result, err
	}
	current, err = os.ReadFile(path)
	if err != nil {
		return result, err
	}
	if !bytes.Equal(before, current) {
		return result, fmt.Errorf("config changed before replacement; original backup retained at %s", backup.Name())
	}
	if err := os.Rename(temporary.Name(), path); err != nil {
		return result, err
	}
	result.Applied = true
	return result, nil
}
