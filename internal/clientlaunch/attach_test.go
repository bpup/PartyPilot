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
}
