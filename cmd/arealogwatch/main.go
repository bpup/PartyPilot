package main

import (
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"os"
	"reflect"
	"sort"
	"strconv"
	"strings"
	"time"

	"partypilot/internal/arealog"
)

type logFlags map[int]string

func (l *logFlags) String() string { return fmt.Sprint(map[int]string(*l)) }
func (l *logFlags) Set(value string) error {
	slotText, path, ok := strings.Cut(value, "=")
	slot, err := strconv.Atoi(slotText)
	if !ok || err != nil || slot < 1 || slot > 6 || path == "" {
		return errors.New("-log must be SLOT=PATH, with SLOT from 1 to 6")
	}
	if *l == nil {
		*l = make(logFlags)
	}
	if _, exists := (*l)[slot]; exists {
		return fmt.Errorf("duplicate player slot %d", slot)
	}
	(*l)[slot] = path
	return nil
}

type snapshot struct {
	ObservedAt time.Time            `json:"observed_at"`
	Followers  []arealog.Comparison `json:"followers"`
}

func run(paths map[int]string, leader int, maxAge time.Duration, output *json.Encoder) error {
	if err := arealog.ValidatePaths(paths); err != nil {
		return err
	}
	if _, ok := paths[leader]; !ok || maxAge <= 0 {
		return errors.New("leader must be listed and max-age must be positive")
	}
	slots := make([]int, 0, len(paths))
	for slot := range paths {
		slots = append(slots, slot)
	}
	sort.Ints(slots)
	tails := make(map[int]*arealog.Tail)
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
	latest := make(map[int]*arealog.Signal)
	var previous []arealog.Comparison
	ticker := time.NewTicker(250 * time.Millisecond)
	defer ticker.Stop()
	for {
		now := time.Now().UTC()
		if err := arealog.ValidatePaths(paths); err != nil {
			return err
		}
		for _, slot := range slots {
			lines, err := tails[slot].Poll()
			if err != nil {
				return fmt.Errorf("slot %d log: %w", slot, err)
			}
			for _, line := range lines {
				if signal, ok := arealog.ParseGeneration(line, slot, now); ok {
					latest[slot] = &signal
				}
			}
		}
		current := make([]arealog.Comparison, 0, len(slots)-1)
		for _, slot := range slots {
			if slot != leader {
				current = append(current, arealog.Compare(now, maxAge, leader, slot, latest[leader], latest[slot]))
			}
		}
		if !reflect.DeepEqual(previous, current) {
			if err := output.Encode(snapshot{ObservedAt: now, Followers: current}); err != nil {
				return err
			}
			previous = current
		}
		<-ticker.C
	}
}

func main() {
	var logs logFlags
	flag.Var(&logs, "log", "repeat for each player: SLOT=Client.txt path")
	leader := flag.Int("leader", 0, "leader player slot")
	maxAge := flag.Duration("max-age", 0, "maximum age of new log evidence")
	flag.Parse()
	fmt.Fprintln(os.Stderr, "Read-only area log monitor; matching IDs and seeds are candidates, not game-verified instance identity.")
	if err := run(logs, *leader, *maxAge, json.NewEncoder(os.Stdout)); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
