package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"net"
	"os"

	"partypilot/internal/telemetry"
)

func main() {
	address := flag.String("listen", "127.0.0.1:47617", "local telemetry address")
	flag.Parse()
	parsed, err := net.ResolveUDPAddr("udp", *address)
	if err != nil || parsed.IP == nil || !parsed.IP.IsLoopback() {
		fmt.Fprintln(os.Stderr, "listen address must be a loopback UDP address")
		os.Exit(2)
	}
	conn, err := net.ListenUDP("udp", parsed)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	defer conn.Close()
	fmt.Fprintln(os.Stderr, "Reading diagnostic telemetry; press Ctrl+C to stop.")
	data := make([]byte, 64*1024)
	for {
		count, _, err := conn.ReadFromUDP(data)
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
		item, err := telemetry.Decode(data[:count])
		if err != nil {
			fmt.Fprintln(os.Stderr, "discarded invalid sample")
			continue
		}
		normalized, _ := json.Marshal(item)
		fmt.Println(string(normalized))
	}
}
