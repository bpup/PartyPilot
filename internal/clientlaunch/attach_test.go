package clientlaunch

import (
	"os"
	"path/filepath"
	"testing"
)

func TestSelectAttachedProcessRequiresUniqueExecutable(t *testing.T) {
	root := t.TempDir()
	first := filepath.Join(root, "first", "PathOfExile_x64.exe")
	second := filepath.Join(root, "second", "PathOfExile_x64.exe")
	for _, path := range []string{first, second} {
		if err := os.MkdirAll(filepath.Dir(path), 0700); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(path, nil, 0600); err != nil {
			t.Fatal(err)
		}
	}
	if pid, err := SelectAttachedProcess(first, map[int]string{101: first, 102: second}); err != nil || pid != 101 {
		t.Fatalf("exact executable path did not select the intended client: pid=%d error=%v", pid, err)
	}
	if _, err := SelectAttachedProcess(first, map[int]string{101: first, 102: first}); err == nil {
		t.Fatal("two processes using one installation were accepted")
	}
	if _, err := SelectAttachedProcess(first, map[int]string{102: second}); err == nil {
		t.Fatal("unrelated installation was attached")
	}
	if _, err := SelectAttachedProcess(first, map[int]string{0: first}); err == nil {
		t.Fatal("invalid process ID was attached")
	}
	if pid, err := SelectAttachedPID(first, 102, map[int]string{101: first, 102: first}); err != nil || pid != 102 {
		t.Fatalf("explicit same-installation process selection failed: pid=%d error=%v", pid, err)
	}
	if _, err := SelectAttachedPID(first, 102, map[int]string{101: first, 102: second}); err == nil {
		t.Fatal("explicit selection accepted another installation")
	}
}

func TestAttachRequiresDistinctExplicitPIDsForSharedInstallation(t *testing.T) {
	root := t.TempDir()
	path := filepath.Join(root, "PathOfExile_x64.exe")
	if err := os.WriteFile(path, nil, 0600); err != nil {
		t.Fatal(err)
	}
	paths, err := ValidateAttach([]string{path, path}, 1)
	if err != nil {
		t.Fatal(err)
	}
	for _, selected := range []map[int]int{nil, {1: 101}, {1: 101, 2: 101}, {1: 101, 3: 102}} {
		if err := ValidateAttachPIDs(paths, selected); err == nil {
			t.Fatalf("ambiguous binding accepted: %+v", selected)
		}
	}
	if err := ValidateAttachPIDs(paths, map[int]int{1: 101, 2: 102}); err != nil {
		t.Fatal(err)
	}
}
