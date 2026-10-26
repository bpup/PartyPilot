package clientprocess

import (
	"path/filepath"
	"strings"
)

var knownGameNames = map[string]bool{
	"pathofexile": true, "pathofexile_x64": true,
	"pathofexilesteam": true, "pathofexile_x64steam": true,
	"pathofexileegs": true, "pathofexile_x64egs": true,
}

func IsKnownGameExecutable(path string) bool {
	if !strings.EqualFold(filepath.Ext(path), ".exe") {
		return false
	}
	name := strings.ToLower(strings.TrimSuffix(filepath.Base(path), filepath.Ext(path)))
	return knownGameNames[name]
}
