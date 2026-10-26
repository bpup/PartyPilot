package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"os"
	"path/filepath"
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

type attachPIDs map[int]int

func (value *attachPIDs) String() string { return fmt.Sprint(map[int]int(*value)) }
func (value *attachPIDs) Set(raw string) error {
	slotText, pidText, found := strings.Cut(raw, "=")
	slot, slotErr := strconv.Atoi(slotText)
	pid, pidErr := strconv.Atoi(pidText)
	if !found || slotErr != nil || pidErr != nil || slot < 1 || slot > 3 || pid <= 0 {
		return errors.New("-pid requires SLOT=positive-PID for client 1–3")
	}
	if *value == nil {
		*value = make(map[int]int)
	}
	if (*value)[slot] != 0 {
		return fmt.Errorf("duplicate process selection for client %d", slot)
	}
	(*value)[slot] = pid
	return nil
}

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
	self, _ := os.Executable()
	defaultRuntime := filepath.Join(filepath.Dir(self), "native", "PartyPilot.MultiInstance.dll")
	var paths executables
	var uris launchURIs
	var pids attachPIDs
	flag.Var(&paths, "exe", "repeat for each game client in launch order")
	flag.Var(&uris, "uri", "optional SLOT=Steam-or-Epic-launch-URI, bound to the matching -exe")
	flag.Var(&pids, "pid", "optional SLOT=PID for -attach; required when clients share one installation")
	runtime := flag.String("multi-instance-runtime", defaultRuntime, "independent mutex runtime for extra standalone clients from one installation")
	preResumeClient3 := flag.Bool("pre-resume-client3", false, "wait for Client 3 private input preparation before resuming its standalone process")
	check := flag.Bool("check", false, "validate paths and layout without launching")
	attach := flag.Bool("attach", false, "bind one existing game window to each configured executable")
	timeout := flag.Duration("timeout", 60*time.Second, "time to wait for each game window")
	applyLayout := flag.Bool("apply-layout", false, "arrange windows after each client is ready")
	displaysFile := flag.String("displays-file", "", "offline display fixture for -check only")
	monitors := flag.String("monitors", "", "one display index per client, required with -apply-layout")
	mode := flag.String("mode", "stacked", "stacked, tiled, or multiple")
	size := flag.String("size", "small", "stacked size")
	align := flag.String("align", "center", "stacked alignment")
	flag.Parse()
	runtimePath, pathErr := filepath.Abs(*runtime)
	if pathErr != nil {
		fail(pathErr)
	}
	if *attach && (*check || len(uris) != 0) {
		fail(errors.New("-attach cannot be combined with -check or -uri"))
	}
	if len(pids) != 0 && !*attach {
		fail(errors.New("-pid requires -attach"))
	}
	if *preResumeClient3 && (*attach || *check) {
		fail(errors.New("-pre-resume-client3 requires a game launch"))
	}
	var validated []string
	var err error
	if *attach {
		validated, err = clientlaunch.ValidateAttach(paths, *timeout)
	} else {
		validated, err = clientlaunch.ValidateStart(paths, *timeout, runtimePath)
	}
	if err != nil {
		fail(err)
	}
	if *preResumeClient3 && len(validated) != 3 {
		fail(errors.New("-pre-resume-client3 requires exactly three clients"))
	}
	if *attach {
		if err := clientlaunch.ValidateAttachPIDs(validated, pids); err != nil {
			fail(err)
		}
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
	var bound clientlaunch.Result
	if *attach {
		bound, err = clientlaunch.Attach(validated, pids)
	} else {
		if *preResumeClient3 {
			bound, err = clientlaunch.StartWithPreResume(validated, uris, *timeout,
				runtimePath, func(pid int) error { return waitForClient3Preparation(pid, validated[2]) })
		} else {
			bound, err = clientlaunch.Start(validated, uris, *timeout, runtimePath)
		}
	}
	response.Clients = bound.Clients
	if err != nil {
		response.Error = err.Error()
		write(response)
		fail(err)
	}
	if *applyLayout {
		windows := make([]windowlayout.Window, len(bound.Clients))
		for index, client := range bound.Clients {
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

func waitForClient3Preparation(pid int, executable string) error {
	if err := json.NewEncoder(os.Stdout).Encode(struct {
		Event      string `json:"event"`
		PID        int    `json:"pid"`
		Executable string `json:"executable"`
	}{"client3_pre_resume", pid, executable}); err != nil {
		return fmt.Errorf("announce Client 3 preparation: %w", err)
	}
	type reply struct {
		line string
		err  error
	}
	replies := make(chan reply, 1)
	go func() {
		line, err := bufio.NewReader(os.Stdin).ReadString('\n')
		replies <- reply{line, err}
	}()
	select {
	case response := <-replies:
		if response.err != nil || strings.TrimSpace(response.line) != fmt.Sprintf("resume %d", pid) {
			return errors.New("Client 3 private input preparation was not confirmed")
		}
		return nil
	case <-time.After(45 * time.Second):
		return errors.New("Client 3 private input preparation timed out after 45 seconds")
	}
}

func fail(err error) {
	fmt.Fprintln(os.Stderr, err)
	os.Exit(1)
}
