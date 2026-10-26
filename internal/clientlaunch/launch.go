package clientlaunch

import (
	"fmt"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"time"

	"partypilot/internal/clientprocess"
	"partypilot/internal/windowlayout"
)

type Client struct {
	Executable string              `json:"executable"`
	LaunchURI  string              `json:"launch_uri,omitempty"`
	PID        int                 `json:"pid"`
	Window     windowlayout.Window `json:"window"`
}

// ValidateURIs binds each store launch request to one already validated game executable.
// Keys are one-based client slots, matching the launch order shown to the user.
func ValidateURIs(paths []string, uris map[int]string) error {
	for slot, raw := range uris {
		if slot < 1 || slot > len(paths) {
			return fmt.Errorf("launch URI slot %d is outside the selected clients", slot)
		}
		parsed, err := url.Parse(raw)
		if err != nil || parsed.User != nil || parsed.Fragment != "" {
			return fmt.Errorf("client %d has an invalid launch URI", slot)
		}
		name := strings.ToLower(filepath.Base(paths[slot-1]))
		switch strings.ToLower(parsed.Scheme) {
		case "steam":
			if raw != "steam://rungameid/2694490" || !strings.Contains(name, "steam") {
				return fmt.Errorf("client %d requires the PoE2 Steam URI and a Steam game executable", slot)
			}
		case "com.epicgames.launcher":
			values := parsed.Query()
			if parsed.Host != "apps" || !strings.HasPrefix(parsed.EscapedPath(), "/") ||
				len(strings.TrimPrefix(parsed.EscapedPath(), "/")) == 0 ||
				values.Get("action") != "launch" || values.Get("silent") != "true" ||
				len(values) != 2 || len(values["action"]) != 1 || len(values["silent"]) != 1 ||
				!strings.Contains(name, "egs") {
				return fmt.Errorf("client %d requires an Epic game executable and a manifest-derived launch URI", slot)
			}
		default:
			return fmt.Errorf("client %d has an unsupported launch URI scheme", slot)
		}
	}
	return nil
}

type Result struct {
	Clients []Client `json:"clients"`
}

func Validate(paths []string, timeout time.Duration) ([]string, error) {
	return validate(paths, timeout, false)
}

// ValidateAttach permits repeated installation paths because callers can bind
// each slot to a distinct, explicitly identified running process.
func ValidateAttach(paths []string, timeout time.Duration) ([]string, error) {
	return validate(paths, timeout, true)
}

func validate(paths []string, timeout time.Duration, allowShared bool) ([]string, error) {
	if len(paths) < 1 || len(paths) > 3 || timeout <= 0 {
		return nil, fmt.Errorf("configure 1–3 executables and a positive timeout")
	}
	validated := make([]string, 0, len(paths))
	var roots []os.FileInfo
	for _, path := range paths {
		full, err := filepath.Abs(path)
		if err != nil {
			return nil, err
		}
		full, err = filepath.EvalSymlinks(full)
		if err != nil {
			return nil, err
		}
		if !clientprocess.IsKnownGameExecutable(full) {
			return nil, fmt.Errorf("unknown game executable: %s", path)
		}
		info, err := os.Stat(full)
		if err != nil || !info.Mode().IsRegular() {
			return nil, fmt.Errorf("game executable is not a regular file: %s", path)
		}
		root, err := os.Stat(filepath.Dir(full))
		if err != nil {
			return nil, err
		}
		for _, prior := range roots {
			if !allowShared && os.SameFile(root, prior) {
				return nil, fmt.Errorf("multiple clients share one installation directory; independent multi-instance launch is not implemented")
			}
		}
		roots = append(roots, root)
		validated = append(validated, full)
	}
	return validated, nil
}
