package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"math"
	"os"
	"strconv"
	"strings"
	"time"

	"partypilot/internal/live"
	"partypilot/internal/slotaudit"
	"partypilot/internal/telemetry"
)

func main() {
	path := flag.String("input", "", "sensorlog JSONL file")
	fastPath := flag.String("fast-input", "", "orderedmotionlog JSONL file (unverified diagnostic)")
	p1Window := flag.String("p1-only", "", "P1-only movement interval, start:end seconds after first fast frame")
	p2Window := flag.String("p2-only", "", "P2-only movement interval, start:end seconds after first fast frame")
	flag.Parse()
	if *path == "" {
		fmt.Fprintln(os.Stderr, "usage: slotaudit -input sensorlog.jsonl [-fast-input orderedmotionlog.jsonl]")
		os.Exit(2)
	}
	var windows []slotaudit.MovementWindow
	for _, item := range []struct{ label, value string }{{"P1 only", *p1Window}, {"P2 only", *p2Window}} {
		if item.value == "" {
			continue
		}
		window, err := parseMovementWindow(item.label, item.value)
		if err != nil || *fastPath == "" {
			fmt.Fprintln(os.Stderr, "movement intervals require -fast-input and start:end seconds with 0 <= start < end")
			os.Exit(2)
		}
		windows = append(windows, window)
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
		TotalLines         int                                      `json:"totalLines"`
		Invalid            int                                      `json:"invalidLines"`
		FastLines          int                                      `json:"fastLines,omitempty"`
		FastInvalid        int                                      `json:"fastInvalidLines,omitempty"`
		Reports            []slotaudit.Report                       `json:"reports"`
		ControlledMovement map[int][]slotaudit.MovementWindowReport `json:"controlledMovement,omitempty"`
	}{lines, invalid, fastLines, fastInvalid, audit.Reports(), nil}
	if len(windows) > 0 {
		result.ControlledMovement = audit.ControlledMovement(windows)
	}
	if err := json.NewEncoder(os.Stdout).Encode(result); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func parseMovementWindow(label, value string) (slotaudit.MovementWindow, error) {
	parts := strings.Split(value, ":")
	if len(parts) != 2 {
		return slotaudit.MovementWindow{}, fmt.Errorf("invalid movement window")
	}
	start, startErr := strconv.ParseFloat(parts[0], 64)
	end, endErr := strconv.ParseFloat(parts[1], 64)
	if startErr != nil || endErr != nil || math.IsNaN(start) || math.IsNaN(end) ||
		math.IsInf(start, 0) || math.IsInf(end, 0) || start < 0 || end <= start ||
		start > 3600 || end > 3600 {
		return slotaudit.MovementWindow{}, fmt.Errorf("invalid movement window")
	}
	return slotaudit.MovementWindow{Label: label,
		Start: time.Duration(start * float64(time.Second)),
		End:   time.Duration(end * float64(time.Second))}, nil
}
