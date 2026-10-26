//go:build windows

package clientprocess

import (
	"os"
	"testing"
)

func TestExecutablePathForCurrentProcess(t *testing.T) {
	got, err := ExecutablePath(os.Getpid())
	if err != nil {
		t.Fatal(err)
	}
	want, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	gotInfo, err := os.Stat(got)
	if err != nil {
		t.Fatal(err)
	}
	wantInfo, err := os.Stat(want)
	if err != nil {
		t.Fatal(err)
	}
	if !os.SameFile(gotInfo, wantInfo) {
		t.Fatalf("process image %q differs from own executable %q", got, want)
	}
}
