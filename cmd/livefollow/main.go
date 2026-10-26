package main

import (
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"net"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"

	"partypilot/internal/arealog"
	"partypilot/internal/follow"
	"partypilot/internal/live"
	"partypilot/internal/partylog"
	"partypilot/internal/telemetry"
)

type sources map[int]string

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
	return paths, arealog.ValidatePaths(paths)
}

type frame struct {
	Focused  int                    `json:"focused"`
	Commands map[int]follow.Command `json:"commands"`
}

func run(address string, roster map[int]int, paths map[int]string, leader int, engine *live.Engine) error {
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
	data := make([]byte, 4096)
	encoder := json.NewEncoder(os.Stdout)
	for {
		_ = conn.SetReadDeadline(time.Now().Add(100 * time.Millisecond))
		count, _, err := conn.ReadFromUDP(data)
		if err == nil {
			if sample, decodeErr := telemetry.Decode(data[:count]); decodeErr == nil {
				engine.ObserveSample(sample)
			}
		} else if timeout, ok := err.(net.Error); !ok || !timeout.Timeout() {
			return err
		}
		if err := arealog.ValidatePaths(paths); err != nil {
			return err
		}
		now := time.Now().UTC()
		for _, slot := range slots {
			lines, err := tails[slot].Poll()
			if err != nil {
				return fmt.Errorf("slot %d log: %w", slot, err)
			}
			for _, line := range lines {
				if signal, ok := arealog.ParseGeneration(line, slot, now); ok {
					engine.ObserveArea(signal)
				}
				if event, ok := partylog.Parse(line, slot); ok {
					engine.ObserveParty(event)
				}
			}
		}
		if err := encoder.Encode(frame{Focused: leader, Commands: engine.Step(now)}); err != nil {
			return err
		}
	}
}

func main() {
	rosterText := flag.String("roster", "", "explicit slot:process pairs, one player per client")
	leader := flag.Int("leader", 0, "leader slot")
	stop := flag.Float64("stop", -1, "stop distance")
	resume := flag.Float64("resume", -1, "resume distance")
	catchup := flag.Float64("catchup", -1, "catch-up distance")
	maxAge := flag.Duration("max-age", 0, "maximum sensor age")
	logAge := flag.Duration("log-max-age", 0, "maximum area-log signal age")
	leaderName := flag.String("leader-name", "", "configured character name of the followed leader, for departure detection")
	address := flag.String("listen", "127.0.0.1:47617", "loopback sensor address")
	var logs, exes sources
	flag.Var(&logs, "log", "SLOT=Client.txt path")
	flag.Var(&exes, "exe", "SLOT=game executable path")
	flag.Parse()
	roster, err := parseRoster(*rosterText)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	paths, err := resolveLogs(roster, logs, exes)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	config := follow.Config{StopDistance: *stop, ResumeDistance: *resume, CatchUpDistance: *catchup, MaxAge: *maxAge}
	engine, err := live.New(roster, *leader, config, *logAge)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	engine.SetLeaderName(*leaderName)
	fmt.Fprintln(os.Stderr, "Live follow decision stream; Windows controller and PoE2 behavior remain unverified.")
	if err := run(*address, roster, paths, *leader, engine); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
