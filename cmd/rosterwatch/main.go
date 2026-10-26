package main

import (
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"net"
	"os"
	"reflect"
	"strconv"
	"strings"
	"time"

	"partypilot/internal/telemetry"
)

func parseRoster(value string) (map[int]int, error) {
	if value == "" {
		return nil, errors.New("empty roster")
	}
	roster := make(map[int]int)
	for _, entry := range strings.Split(value, ",") {
		parts := strings.Split(entry, ":")
		if len(parts) != 2 {
			return nil, fmt.Errorf("invalid roster entry %q", entry)
		}
		slot, slotErr := strconv.Atoi(parts[0])
		pid, pidErr := strconv.Atoi(parts[1])
		if slotErr != nil || pidErr != nil || slot < 1 || slot > 6 || pid <= 0 {
			return nil, fmt.Errorf("invalid roster entry %q", entry)
		}
		if _, exists := roster[slot]; exists {
			return nil, fmt.Errorf("duplicate player slot %d", slot)
		}
		roster[slot] = pid
	}
	return roster, nil
}

func main() {
	rosterFlag := flag.String("roster", "", "explicit player:process pairs, e.g. 1:1234,2:5678")
	leaderSlot := flag.Int("leader", 0, "leader player slot")
	maxAge := flag.Duration("max-age", 0, "maximum telemetry age, such as 750ms")
	address := flag.String("listen", "127.0.0.1:47617", "loopback telemetry address")
	flag.Parse()
	roster, err := parseRoster(*rosterFlag)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	monitor, ok := telemetry.NewRosterMonitor(roster, *leaderSlot, *maxAge)
	if !ok {
		fmt.Fprintln(os.Stderr, "roster requires 1-6 unique player slots and process IDs, a listed leader, and positive max-age")
		os.Exit(2)
	}
	endpoint, err := net.ResolveUDPAddr("udp", *address)
	if err != nil || endpoint.IP == nil || !endpoint.IP.IsLoopback() {
		fmt.Fprintln(os.Stderr, "listen address must be loopback")
		os.Exit(2)
	}
	conn, err := net.ListenUDP("udp", endpoint)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	defer conn.Close()
	fmt.Fprintln(os.Stderr, "Read-only roster monitor started; area/hash matches remain candidates; no controller output.")
	encoder := json.NewEncoder(os.Stdout)
	data := make([]byte, 4096)
	var previous *telemetry.RosterSnapshot
	for {
		_ = conn.SetReadDeadline(time.Now().Add(250 * time.Millisecond))
		count, _, readErr := conn.ReadFromUDP(data)
		accepted := false
		if readErr == nil {
			item, decodeErr := telemetry.Decode(data[:count])
			accepted = decodeErr == nil && monitor.Update(item)
		} else if timeout, ok := readErr.(net.Error); !ok || !timeout.Timeout() {
			fmt.Fprintln(os.Stderr, readErr)
			os.Exit(1)
		}
		snapshot := monitor.Snapshot(time.Now().UTC())
		if accepted || previous == nil || !reflect.DeepEqual(snapshot, *previous) {
			if err := encoder.Encode(snapshot); err != nil {
				fmt.Fprintln(os.Stderr, err)
				os.Exit(1)
			}
		}
		previous = &snapshot
	}
}
