package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"os"

	"partypilot/internal/slotaudit"
	"partypilot/internal/telemetry"
)

func main() {
	path := flag.String("input", "", "sensorlog JSONL file")
	flag.Parse()
	if *path == "" {
		fmt.Fprintln(os.Stderr, "usage: slotaudit -input sensorlog.jsonl")
		os.Exit(2)
	}
	file, err := os.Open(*path)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	defer file.Close()
	scanner := bufio.NewScanner(file)
	scanner.Buffer(make([]byte, 64*1024), 1024*1024)
	audit := slotaudit.New()
	lines, invalid := 0, 0
	for scanner.Scan() {
		lines++
		sample, err := telemetry.Decode(scanner.Bytes())
		if err != nil {
			invalid++
			continue
		}
		audit.Observe(sample)
	}
	if err := scanner.Err(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	result := struct {
		TotalLines int                `json:"totalLines"`
		Invalid    int                `json:"invalidLines"`
		Reports    []slotaudit.Report `json:"reports"`
	}{lines, invalid, audit.Reports()}
	if err := json.NewEncoder(os.Stdout).Encode(result); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
