package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"net"
	"os"
	"time"

	"partypilot/internal/telemetry"
)

func main() {
	leader := flag.Int("leader-pid", 0, "leader game process ID")
	follower := flag.Int("follower-pid", 0, "follower game process ID")
	maxAge := flag.Duration("max-age", 0, "maximum telemetry age, such as 750ms")
	address := flag.String("listen", "127.0.0.1:47617", "loopback telemetry address")
	flag.Parse()

	monitor, ok := telemetry.NewMonitor(*leader, *follower, *maxAge)
	if !ok {
		fmt.Fprintln(os.Stderr, "specify distinct positive process IDs and a positive max-age")
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
	fmt.Fprintln(os.Stderr, "Read-only party monitor started; no controller output.")
	encoder := json.NewEncoder(os.Stdout)
	data := make([]byte, 64*1024)
	previous := telemetry.Status("")
	for {
		_ = conn.SetReadDeadline(time.Now().Add(250 * time.Millisecond))
		count, _, err := conn.ReadFromUDP(data)
		if err == nil {
			item, decodeErr := telemetry.Decode(data[:count])
			if decodeErr != nil || !monitor.Update(item) {
				continue
			}
		} else if timeout, ok := err.(net.Error); !ok || !timeout.Timeout() {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
		comparison := monitor.Compare(time.Now().UTC())
		if err == nil || comparison.Status != previous {
			if encodeErr := encoder.Encode(comparison); encodeErr != nil {
				fmt.Fprintln(os.Stderr, encodeErr)
				os.Exit(1)
			}
		}
		previous = comparison.Status
	}
}
