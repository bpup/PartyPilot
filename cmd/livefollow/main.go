package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"math"
	"net"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/clientprocess"
	"partypilot/internal/flask"
	"partypilot/internal/follow"
	"partypilot/internal/live"
	"partypilot/internal/telemetry"
)

type sources map[int]string

type memberNames map[int]string

type followTargets map[int]int

type stopDistances map[int]float64

type roleBindings []live.RoleBinding

func (bindings *roleBindings) String() string { return fmt.Sprint([]live.RoleBinding(*bindings)) }
func (bindings *roleBindings) Set(value string) error {
	parts := strings.Split(value, ":")
	if len(parts) != 4 {
		return errors.New("role binding must be ROLE:CLIENT:LOCAL:PID")
	}
	values := [4]int{}
	for index, part := range parts {
		parsed, err := strconv.Atoi(part)
		if err != nil || parsed <= 0 {
			return errors.New("role binding must contain positive integers")
		}
		values[index] = parsed
	}
	*bindings = append(*bindings, live.RoleBinding{Slot: values[0], Client: values[1],
		LocalPlayer: values[2], ProcessID: values[3]})
	return nil
}

func clientRoster(bindings []live.RoleBinding) (map[int]int, error) {
	clients := make(map[int]int)
	for _, binding := range bindings {
		if existing := clients[binding.Client]; existing != 0 && existing != binding.ProcessID {
			return nil, fmt.Errorf("client %d has multiple processes", binding.Client)
		}
		clients[binding.Client] = binding.ProcessID
	}
	if len(clients) < 1 || len(clients) > 3 {
		return nil, errors.New("ordered role bindings need 1–3 clients")
	}
	return clients, nil
}

func (distances *stopDistances) String() string { return fmt.Sprint(map[int]float64(*distances)) }
func (distances *stopDistances) Set(value string) error {
	slotText, distanceText, ok := strings.Cut(value, "=")
	slot, slotErr := strconv.Atoi(slotText)
	distance, distanceErr := strconv.ParseFloat(distanceText, 64)
	if !ok || slotErr != nil || distanceErr != nil || slot < 1 || slot > 6 ||
		math.IsNaN(distance) || math.IsInf(distance, 0) || distance < 0 || distance > 200 {
		return errors.New("stop distance must be SLOT=VALUE, with VALUE from 0 to 200")
	}
	if *distances == nil {
		*distances = make(stopDistances)
	}
	if _, exists := (*distances)[slot]; exists {
		return fmt.Errorf("duplicate stop distance for slot %d", slot)
	}
	(*distances)[slot] = distance
	return nil
}

func (targets *followTargets) String() string { return fmt.Sprint(map[int]int(*targets)) }
func (targets *followTargets) Set(value string) error {
	slotText, targetText, ok := strings.Cut(value, "=")
	slot, slotErr := strconv.Atoi(slotText)
	targetValue, targetErr := strconv.ParseInt(targetText, 10, 32)
	if !ok || slotErr != nil || targetErr != nil || slot < 1 || slot > 6 {
		return errors.New("follow target must be SLOT=TARGET, with 0 for Current MAIN")
	}
	if *targets == nil {
		*targets = make(followTargets)
	}
	if _, exists := (*targets)[slot]; exists {
		return fmt.Errorf("duplicate follow target for slot %d", slot)
	}
	(*targets)[slot] = int(targetValue)
	return nil
}

func (names *memberNames) String() string { return fmt.Sprint(map[int]string(*names)) }
func (names *memberNames) Set(value string) error {
	slotText, name, ok := strings.Cut(value, "=")
	slot, err := strconv.Atoi(slotText)
	name = strings.TrimSpace(name)
	if !ok || err != nil || slot < 1 || slot > 6 || name == "" || len(name) > 128 {
		return errors.New("member name must be SLOT=NAME")
	}
	if *names == nil {
		*names = make(memberNames)
	}
	if _, exists := (*names)[slot]; exists {
		return fmt.Errorf("duplicate member name for slot %d", slot)
	}
	(*names)[slot] = name
	return nil
}

func (s *sources) String() string { return fmt.Sprint(map[int]string(*s)) }
func (s *sources) Set(value string) error {
	slotText, path, ok := strings.Cut(value, "=")
	slot, err := strconv.Atoi(slotText)
	if !ok || err != nil || slot < 1 || slot > 6 || path == "" {
		return errors.New("source must be SLOT=PATH")
	}
	if *s == nil {
		*s = make(sources)
	}
	if _, exists := (*s)[slot]; exists {
		return fmt.Errorf("duplicate source for slot %d", slot)
	}
	(*s)[slot] = path
	return nil
}

func parseRoster(value string) (map[int]int, error) {
	roster := make(map[int]int)
	for _, entry := range strings.Split(value, ",") {
		slotText, pidText, ok := strings.Cut(entry, ":")
		slot, slotErr := strconv.Atoi(slotText)
		pid, pidErr := strconv.Atoi(pidText)
		if !ok || slotErr != nil || pidErr != nil || slot < 1 || slot > 6 || pid <= 0 {
			return nil, fmt.Errorf("invalid roster entry %q", entry)
		}
		if _, exists := roster[slot]; exists {
			return nil, fmt.Errorf("duplicate slot %d", slot)
		}
		roster[slot] = pid
	}
	if len(roster) < 2 || len(roster) > 3 {
		return nil, errors.New("current sensor supports 2–3 clients, one player each")
	}
	return roster, nil
}

func resolveLogs(roster map[int]int, logs, exes sources) (map[int]string, error) {
	return resolveLogPaths(roster, logs, exes, arealog.ValidatePaths)
}

func resolveLogPaths(roster map[int]int, logs, exes sources,
	validate func(map[int]string) error) (map[int]string, error) {
	paths := make(map[int]string, len(roster))
	for slot := range roster {
		if path, exists := logs[slot]; exists {
			paths[slot] = path
		}
		if exe, exists := exes[slot]; exists {
			if _, duplicate := paths[slot]; duplicate {
				return nil, fmt.Errorf("slot %d has both -log and -exe", slot)
			}
			paths[slot] = filepath.Join(filepath.Dir(exe), "logs", "Client.txt")
		}
		if paths[slot] == "" {
			return nil, fmt.Errorf("slot %d requires a log source", slot)
		}
	}
	if len(logs)+len(exes) != len(roster) {
		return nil, errors.New("log source slot is not in roster")
	}
	return paths, validate(paths)
}

func verifyProcessBindings(roster map[int]int, paths map[int]string) error {
	if !clientprocess.Supported {
		return nil
	}
	for slot, pid := range roster {
		executable, err := clientprocess.ExecutablePath(pid)
		if err != nil {
			return fmt.Errorf("slot %d process %d: %w", slot, pid, err)
		}
		if err := clientprocess.ValidateLogBinding(executable, paths[slot]); err != nil {
			return fmt.Errorf("slot %d process/log binding: %w", slot, err)
		}
	}
	return nil
}

type frame struct {
	Focused  int                    `json:"focused"`
	Commands map[int]follow.Command `json:"commands"`
	Flasks   []flask.Activation     `json:"flasks,omitempty"`
}

type menuInput struct {
	Type  string `json:"type"`
	Slot  int    `json:"slot"`
	State string `json:"state"`
	Err   error  `json:"-"`
}

func readMenuInput(reader io.Reader, output chan<- menuInput) {
	defer close(output)
	scanner := bufio.NewScanner(reader)
	for scanner.Scan() {
		var input menuInput
		if err := json.Unmarshal(scanner.Bytes(), &input); err != nil ||
			(input.Type != "menu_guard" && input.Type != "manual_focus") {
			output <- menuInput{Err: errors.New("invalid live control command")}
			return
		}
		output <- input
	}
	if err := scanner.Err(); err != nil {
		output <- menuInput{Err: err}
	}
}

func applyMenuInput(session *live.FlaskSession, engine *live.Engine, input <-chan menuInput) error {
	if input == nil {
		return nil
	}
	for {
		select {
		case command, open := <-input:
			if !open {
				return errors.New("menu guard input closed")
			}
			if command.Err != nil {
				return command.Err
			}
			switch command.Type {
			case "menu_guard":
				if session == nil {
					return errors.New("menu guard is not enabled")
				}
				if err := session.SetMenuGuard(command.Slot, command.State); err != nil {
					return err
				}
			case "manual_focus":
				if err := engine.ObserveManualFocus(command.Slot); err != nil {
					return err
				}
			default:
				return errors.New("invalid live control command")
			}
		default:
			return nil
		}
	}
}

func run(address string, roster map[int]int, paths map[int]string, leader int, engine *live.Engine,
	flasks *live.FlaskSession, menuInput <-chan menuInput, ordered bool) error {
	endpoint, err := net.ResolveUDPAddr("udp", address)
	if err != nil || endpoint.IP == nil || !endpoint.IP.IsLoopback() {
		return errors.New("listen address must be loopback")
	}
	conn, err := net.ListenUDP("udp", endpoint)
	if err != nil {
		return err
	}
	defer conn.Close()
	slots := make([]int, 0, len(roster))
	for slot := range roster {
		slots = append(slots, slot)
	}
	sort.Ints(slots)
	tails := make(map[int]*arealog.Tail, len(slots))
	for _, slot := range slots {
		tail, err := arealog.OpenTail(paths[slot])
		if err != nil {
			for _, opened := range tails {
				opened.Close()
			}
			return err
		}
		tails[slot] = tail
	}
	defer func() {
		for _, tail := range tails {
			tail.Close()
		}
	}()
	data := make([]byte, 64*1024)
	encoder := json.NewEncoder(os.Stdout)
	fail := func(reason string, err error) error {
		engine.Stop(reason)
		_ = encoder.Encode(frame{Focused: engine.Leader(), Commands: engine.Step(time.Now().UTC())})
		return err
	}
	for {
		if err := applyMenuInput(flasks, engine, menuInput); err != nil {
			return fail("menu guard input failed", err)
		}
		var activations []flask.Activation
		_ = conn.SetReadDeadline(time.Now().Add(100 * time.Millisecond))
		count, _, err := conn.ReadFromUDP(data)
		if err == nil {
			if ordered {
				if orderedFrame, decodeErr := live.DecodeOrderedClient(data[:count], time.Now().UTC()); decodeErr == nil {
					engine.ObserveOrderedClient(orderedFrame)
				}
			} else if sample, decodeErr := telemetry.Decode(data[:count]); decodeErr == nil {
				sample.ReceivedAt = time.Now().UTC()
				if engine.ObserveSample(sample) && flasks != nil {
					activations, err = flasks.ObserveSample(sample, time.Now().UTC())
					if err != nil {
						return fail("flask decision failed", err)
					}
				}
			}
		} else if timeout, ok := err.(net.Error); !ok || !timeout.Timeout() {
			return fail("sensor read failed", err)
		}
		var pathErr error
		if ordered {
			pathErr = arealog.ValidateClientPaths(paths)
		} else {
			pathErr = arealog.ValidatePaths(paths)
		}
		if err := pathErr; err != nil {
			return fail("log source changed", err)
		}
		if err := verifyProcessBindings(roster, paths); err != nil {
			return fail("process/log binding changed", err)
		}
		now := time.Now().UTC()
		for _, slot := range slots {
			lines, err := tails[slot].Poll()
			if err != nil {
				return fail("log read failed", fmt.Errorf("slot %d log: %w", slot, err))
			}
			for _, line := range lines {
				engine.ObserveClientLogLine(roster[slot], line, now)
			}
		}
		if err := encoder.Encode(frame{Focused: engine.Leader(), Commands: engine.Step(now), Flasks: activations}); err != nil {
			return err
		}
	}
}

func main() {
	rosterText := flag.String("roster", "", "explicit slot:process pairs, one player per client")
	orderedInput := flag.Bool("experimental-ordered-input", false, "explicit ordered 1–2 player frames; requires independently verified source")
	leader := flag.Int("leader", 0, "leader slot")
	stop := flag.Float64("stop", -1, "stop distance")
	catchup := flag.Float64("catchup", -1, "catch-up distance")
	maxAge := flag.Duration("max-age", 0, "maximum sensor age")
	logAge := flag.Duration("log-max-age", 0, "maximum area-log signal age")
	leaderName := flag.String("leader-name", "", "configured character name of the followed leader, for departure detection")
	lastFocusedLeads := flag.Bool("last-focused-leads", false, "use manually focused player as the current follow MAIN")
	autoFlask := flag.Bool("auto-flask", false, "enable life and mana flask decisions for explicitly bound clients")
	address := flag.String("listen", "127.0.0.1:47617", "loopback sensor address")
	var logs, exes, terrainBindings sources
	var names memberNames
	var targets followTargets
	var playerStops stopDistances
	var bindings roleBindings
	flag.Var(&bindings, "role-bind", "ROLE:CLIENT:LOCAL:PID; ordered input only, repeat for every role")
	flag.Var(&logs, "log", "SLOT=Client.txt path")
	flag.Var(&exes, "exe", "SLOT=game executable path")
	flag.Var(&terrainBindings, "terrain-binding", "SLOT=JSON path with explicit capture and live instance mapping")
	flag.Var(&names, "member-name", "SLOT=confirmed character name for departure detection")
	flag.Var(&targets, "follow-target", "SLOT=TARGET; 0 means Current MAIN")
	flag.Var(&playerStops, "stop-player", "SLOT=VALUE; individual stop distance from 0 to 200")
	flag.Parse()
	if *orderedInput != (len(bindings) > 0) || *orderedInput && *rosterText != "" ||
		!(*orderedInput) && *rosterText == "" || *orderedInput && *autoFlask {
		fmt.Fprintln(os.Stderr, "choose either -roster or -experimental-ordered-input with -role-bind; ordered auto-flask is unavailable")
		os.Exit(2)
	}
	var roster map[int]int
	var err error
	if *orderedInput {
		roster, err = clientRoster(bindings)
	} else {
		roster, err = parseRoster(*rosterText)
	}
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	var paths map[int]string
	if *orderedInput {
		paths, err = resolveLogPaths(roster, logs, exes, arealog.ValidateClientPaths)
	} else {
		paths, err = resolveLogs(roster, logs, exes)
	}
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	if err := verifyProcessBindings(roster, paths); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	config := follow.Config{StopDistance: *stop, ResumeDistance: *stop + 3, CatchUpDistance: *catchup,
		MaxAge: *maxAge, TargetTrail: true, AsyncTargetPath: true}
	var engine *live.Engine
	if *orderedInput {
		engine, err = live.NewWithBindings(bindings, *leader, config, *logAge)
	} else {
		engine, err = live.New(roster, *leader, config, *logAge)
	}
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	defer engine.Close()
	for slot, path := range terrainBindings {
		binding, loadErr := live.LoadTerrainBinding(path)
		if loadErr != nil {
			fmt.Fprintln(os.Stderr, loadErr)
			os.Exit(2)
		}
		if err := engine.SetTerrainBinding(slot, binding); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(2)
		}
	}
	engine.SetLeaderName(*leaderName)
	for slot, name := range names {
		if err := engine.SetMemberName(slot, name); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(2)
		}
	}
	engine.SetLastFocusedLeads(*lastFocusedLeads)
	for slot, target := range targets {
		if err := engine.SetFollowTarget(slot, target); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(2)
		}
	}
	for slot, distance := range playerStops {
		if err := engine.SetStopDistance(slot, distance); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(2)
		}
	}
	var flaskSession *live.FlaskSession
	var menuCommands <-chan menuInput
	if *autoFlask {
		flaskSession, err = live.NewFlaskSession(roster, *maxAge)
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(2)
		}
	}
	if *autoFlask || *lastFocusedLeads {
		commands := make(chan menuInput, 16)
		menuCommands = commands
		go readMenuInput(os.Stdin, commands)
	}
	fmt.Fprintln(os.Stderr, "Live follow decision stream; Windows controller and PoE2 behavior remain unverified.")
	if err := run(*address, roster, paths, *leader, engine, flaskSession, menuCommands, *orderedInput); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
