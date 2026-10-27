package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"time"

	"partypilot/internal/live"
	"partypilot/internal/slotaudit"
	"partypilot/internal/telemetry"
)

func main() {
	path := flag.String("input", "", "sensorlog JSONL file")
	fastPath := flag.String("fast-input", "", "orderedmotionlog JSONL file (unverified diagnostic)")
	flag.Parse()
	if *path == "" {
		fmt.Fprintln(os.Stderr, "usage: slotaudit -input sensorlog.jsonl [-fast-input orderedmotionlog.jsonl]")
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
	fastLines, fastInvalid := 0, 0
	if *fastPath != "" {
		fastFile, err := os.Open(*fastPath)
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
		defer fastFile.Close()
		fastScanner := bufio.NewScanner(fastFile)
		fastScanner.Buffer(make([]byte, 64*1024), 1024*1024)
		for fastScanner.Scan() {
			fastLines++
			frame, err := live.DecodeOrderedClient(fastScanner.Bytes(), time.Now().UTC())
			if err != nil || !audit.ObserveFast(frame) {
				fastInvalid++
			}
		}
		if err := fastScanner.Err(); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
	}
	result := struct {
		TotalLines  int                `json:"totalLines"`
		Invalid     int                `json:"invalidLines"`
		FastLines   int                `json:"fastLines,omitempty"`
		FastInvalid int                `json:"fastInvalidLines,omitempty"`
		Reports     []slotaudit.Report `json:"reports"`
	}{lines, invalid, fastLines, fastInvalid, audit.Reports()}
	if err := json.NewEncoder(os.Stdout).Encode(result); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
