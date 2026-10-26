package main

import (
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"os"
	"strconv"
	"strings"
	"time"

	"partypilot/internal/clientlaunch"
	"partypilot/internal/windowlayout"
)

type executables []string

func (e *executables) String() string { return fmt.Sprint([]string(*e)) }
func (e *executables) Set(path string) error {
	if path == "" {
		return errors.New("-exe requires a path")
	}
	*e = append(*e, path)
	return nil
}

type launchURIs map[int]string

func (value *launchURIs) String() string { return fmt.Sprint(map[int]string(*value)) }
func (value *launchURIs) Set(raw string) error {
	slotText, uri, found := strings.Cut(raw, "=")
	slot, err := strconv.Atoi(slotText)
	if !found || err != nil || slot < 1 || slot > 3 || uri == "" {
		return errors.New("-uri requires SLOT=launch-URI for client 1–3")
	}
	if *value == nil {
		*value = make(map[int]string)
	}
	if _, exists := (*value)[slot]; exists {
		return fmt.Errorf("duplicate launch URI for client %d", slot)
	}
	(*value)[slot] = uri
	return nil
}

func monitorIDs(value string, displays []windowlayout.Display, count int) ([]string, error) {
	parts := strings.Split(value, ",")
	if value == "" || len(parts) != count {
		return nil, errors.New("-monitors must assign one display index per client")
	}
	ids := make([]string, count)
	for index, part := range parts {
		number, err := strconv.Atoi(part)
		if err != nil || number < 1 || number > len(displays) {
			return nil, fmt.Errorf("invalid monitor index %q", part)
		}
		ids[index] = displays[number-1].ID
	}
	return ids, nil
}

type output struct {
	Checked       []string              `json:"checked_executables"`
	LaunchURIs    map[int]string        `json:"checked_launch_uris,omitempty"`
	Clients       []clientlaunch.Client `json:"clients,omitempty"`
	Plan          *windowlayout.Plan    `json:"plan,omitempty"`
	LayoutApplied bool                  `json:"layout_applied"`
	Error         string                `json:"error,omitempty"`
}

func main() {
	var paths executables
	var uris launchURIs
	flag.Var(&paths, "exe", "repeat for each game client in launch order")
	flag.Var(&uris, "uri", "optional SLOT=Steam-or-Epic-launch-URI, bound to the matching -exe")
	check := flag.Bool("check", false, "validate paths and layout without launching")
	timeout := flag.Duration("timeout", 60*time.Second, "time to wait for each game window")
	applyLayout := flag.Bool("apply-layout", false, "arrange windows after each client is ready")
	displaysFile := flag.String("displays-file", "", "offline display fixture for -check only")
	monitors := flag.String("monitors", "", "one display index per client, required with -apply-layout")
	mode := flag.String("mode", "stacked", "stacked, tiled, or multiple")
	size := flag.String("size", "small", "stacked size")
	align := flag.String("align", "center", "stacked alignment")
	flag.Parse()
	validated, err := clientlaunch.Validate(paths, *timeout)
	if err != nil {
		fail(err)
	}
	if err := clientlaunch.ValidateURIs(validated, uris); err != nil {
		fail(err)
	}
	response := output{Checked: validated, LaunchURIs: uris}
	if *displaysFile != "" && !*check {
		fail(errors.New("offline display fixture requires -check"))
	}
	if *applyLayout {
		var displays []windowlayout.Display
		if *displaysFile != "" {
			data, err := os.ReadFile(*displaysFile)
			if err != nil {
				fail(err)
			}
			if err := json.Unmarshal(data, &displays); err != nil {
				fail(err)
			}
		} else {
			var err error
			displays, err = windowlayout.DiscoverDisplays()
			if err != nil {
				fail(err)
			}
		}
		ids, err := monitorIDs(*monitors, displays, len(validated))
		if err != nil {
			fail(err)
		}
		plan, err := windowlayout.Build(displays, windowlayout.Request{
			Mode: *mode, ClientCount: len(validated), DisplayIDs: ids,
			StackedSize: *size, StackedAlign: *align,
		})
		if err != nil {
			fail(err)
		}
		response.Plan = &plan
	} else if *monitors != "" {
		fail(errors.New("-monitors requires -apply-layout"))
	}
	if *check {
		write(response)
		return
	}
	launched, launchErr := clientlaunch.Start(validated, uris, *timeout)
	response.Clients = launched.Clients
	if launchErr != nil {
		response.Error = launchErr.Error()
		write(response)
		fail(launchErr)
	}
	if *applyLayout {
		windows := make([]windowlayout.Window, len(launched.Clients))
		for index, client := range launched.Clients {
			windows[index] = client.Window
		}
		if err := windowlayout.Apply(windows, *response.Plan); err != nil {
			response.Error = err.Error()
			write(response)
			fail(err)
		}
		response.LayoutApplied = true
	}
	write(response)
}

func write(value output) {
	if err := json.NewEncoder(os.Stdout).Encode(value); err != nil {
		fail(err)
	}
}

func fail(err error) {
	fmt.Fprintln(os.Stderr, err)
	os.Exit(1)
}
