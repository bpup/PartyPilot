package clientprocess

import (
	"os"
	"path/filepath"
	"testing"
)

func TestValidateLogBinding(t *testing.T) {
	directory := t.TempDir()
	first := filepath.Join(directory, "first")
	second := filepath.Join(directory, "second")
	for _, root := range []string{first, second} {
		if err := os.MkdirAll(filepath.Join(root, "logs"), 0700); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(filepath.Join(root, "logs", "Client.txt"), nil, 0600); err != nil {
			t.Fatal(err)
		}
	}
	exe := filepath.Join(first, "PathOfExile.exe")
	if err := ValidateLogBinding(exe, filepath.Join(first, "logs", "Client.txt")); err != nil {
		t.Fatal(err)
	}
	if err := ValidateLogBinding(exe, filepath.Join(second, "logs", "Client.txt")); err == nil {
		t.Fatal("accepted another installation's log")
	}
	alias := filepath.Join(directory, "alias.txt")
	if err := os.Symlink(filepath.Join(first, "logs", "Client.txt"), alias); err != nil {
		t.Fatal(err)
	}
	if err := ValidateLogBinding(exe, alias); err != nil {
		t.Fatalf("rejected same underlying log: %v", err)
	}
}
