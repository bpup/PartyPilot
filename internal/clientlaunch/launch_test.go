package clientlaunch

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestLaunchValidationRequiresDistinctInstallations(t *testing.T) {
	directory := t.TempDir()
	var paths []string
	for _, name := range []string{"first", "second"} {
		root := filepath.Join(directory, name)
		if err := os.Mkdir(root, 0700); err != nil {
			t.Fatal(err)
		}
		path := filepath.Join(root, "PathOfExile_x64.exe")
		if err := os.WriteFile(path, nil, 0600); err != nil {
			t.Fatal(err)
		}
		paths = append(paths, path)
	}
	if checked, err := Validate(paths, time.Second); err != nil || len(checked) != 2 {
		t.Fatalf("distinct installation rejected: %v, %v", checked, err)
	}
	if _, err := Validate([]string{paths[0], paths[0]}, time.Second); err == nil {
		t.Fatal("same installation accepted twice")
	}
	if _, err := Validate(paths, 0); err == nil {
		t.Fatal("zero timeout accepted")
	}
	bad := filepath.Join(directory, "first", "other.exe")
	if err := os.WriteFile(bad, nil, 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := Validate([]string{bad}, time.Second); err == nil {
		t.Fatal("unrelated executable accepted")
	}
}

func TestStoreURIsRequireMatchingExecutable(t *testing.T) {
	paths := []string{"/Steam/PathOfExile_x64Steam.exe", "/Epic/PathOfExile_x64EGS.exe"}
	valid := map[int]string{
		1: "steam://rungameid/2694490",
		2: "com.epicgames.launcher://apps/namespace%3Aitem%3APathOfExile2?action=launch&silent=true",
	}
	if err := ValidateURIs(paths, valid); err != nil {
		t.Fatal(err)
	}
	for _, uris := range []map[int]string{
		{3: valid[1]},
		{1: "steam://rungameid/123"},
		{2: "steam://rungameid/2694490"},
		{1: "file:///other.exe"},
		{2: "com.epicgames.launcher://apps/other?action=launch&silent=true&action=other"},
	} {
		if err := ValidateURIs(paths, uris); err == nil {
			t.Fatalf("invalid store binding accepted: %+v", uris)
		}
	}
}
