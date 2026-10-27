package main

import (
	"flag"
	"fmt"
	"net"
	"os"
	"time"

	"partypilot/internal/live"
)

const candidateSource = "unverified_exilecore_local_slot_candidate"

func validCandidate(data []byte, receivedAt time.Time) bool {
	frame, err := live.DecodeOrderedClient(data, receivedAt)
	return err == nil && !frame.PlayerOrderReliable && frame.PlayerOrderSource == candidateSource
}

func main() {
	address := flag.String("listen", "127.0.0.1:47620", "loopback fast-candidate address")
	flag.Parse()
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
	fmt.Fprintln(os.Stderr, "Recording unverified ordered-motion candidates; Ctrl+C to stop.")
	data := make([]byte, 64*1024)
	for {
		count, _, err := conn.ReadFromUDP(data)
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
		if !validCandidate(data[:count], time.Now().UTC()) {
			fmt.Fprintln(os.Stderr, "discarded invalid or trusted-looking candidate packet")
			continue
		}
		if _, err := os.Stdout.Write(append(append([]byte{}, data[:count]...), '\n')); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
	}
}
