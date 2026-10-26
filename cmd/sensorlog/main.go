package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"math"
	"net"
	"os"
	"time"
)

type sample struct {
	ProcessID int       `json:"ProcessId"`
	SeenUTC   time.Time `json:"SeenUtc"`
	AreaName  string    `json:"AreaName"`
	AreaHash  uint32    `json:"AreaHash"`
	X         float64   `json:"X"`
	Y         float64   `json:"Y"`
	Z         float64   `json:"Z"`
}

func valid(item sample) bool {
	return item.ProcessID > 0 && !item.SeenUTC.IsZero() &&
		finite(item.X) && finite(item.Y) && finite(item.Z)
}

func finite(value float64) bool {
	return !math.IsNaN(value) && !math.IsInf(value, 0)
}

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
	data := make([]byte, 4096)
	for {
		count, _, err := conn.ReadFromUDP(data)
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
		var item sample
		if err := json.Unmarshal(data[:count], &item); err != nil || !valid(item) {
			fmt.Fprintln(os.Stderr, "discarded invalid sample")
			continue
		}
		normalized, _ := json.Marshal(item)
		fmt.Println(string(normalized))
	}
}
