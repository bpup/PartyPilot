package clientprocess

import "testing"

func TestKnownGameExecutableNames(t *testing.T) {
	for _, name := range []string{"PathOfExile.exe", "PathOfExile_x64Steam.EXE", "PathOfExileEGS.exe"} {
		if !IsKnownGameExecutable(name) {
			t.Fatalf("rejected known game executable %q", name)
		}
	}
	for _, name := range []string{"other.exe", "PathOfExile.dll", "PathOfExile.exe.txt"} {
		if IsKnownGameExecutable(name) {
			t.Fatalf("accepted unrelated executable %q", name)
		}
	}
}
