package main

import (
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"os"
	"strconv"
	"strings"

	"partypilot/internal/windowlayout"
)

func parseNumbers(value string, allowDuplicates bool) ([]int, error) {
	if value == "" {
		return nil, errors.New("expected 1–3 comma-separated numbers")
	}
	var numbers []int
	seen := make(map[int]bool)
	for _, part := range strings.Split(value, ",") {
		number, err := strconv.Atoi(part)
		if err != nil || number <= 0 || (!allowDuplicates && seen[number]) {
			return nil, fmt.Errorf("invalid or duplicate number %q", part)
		}
		numbers = append(numbers, number)
		seen[number] = true
	}
	if len(numbers) < 1 || len(numbers) > 3 {
		return nil, errors.New("select 1–3 clients")
	}
	return numbers, nil
}

type output struct {
	Displays []windowlayout.Display `json:"displays"`
	Windows  []windowlayout.Window  `json:"windows,omitempty"`
	Plan     *windowlayout.Plan     `json:"plan,omitempty"`
	Applied  bool                   `json:"applied"`
}

func main() {
	list := flag.Bool("list", false, "list display work areas")
	mode := flag.String("mode", "stacked", "stacked, tiled, or multiple")
	pidsText := flag.String("pids", "", "1–3 explicit game process IDs in client order")
	monitorsText := flag.String("monitors", "", "one monitor index per client, e.g. 1,1 or 1,2")
	size := flag.String("size", "small", "stacked size: small, medium, large, full")
	align := flag.String("align", "center", "stacked alignment: left, center, right")
	apply := flag.Bool("apply", false, "place the discovered game windows")
	fixture := flag.String("displays-file", "", "JSON display fixture for offline planning only")
	flag.Parse()
	var displays []windowlayout.Display
	var err error
	if *fixture != "" {
		data, readErr := os.ReadFile(*fixture)
		if readErr != nil {
			fatal(readErr)
		}
		if err = json.Unmarshal(data, &displays); err != nil {
			fatal(err)
		}
	} else {
		displays, err = windowlayout.DiscoverDisplays()
		if err != nil {
			fatal(err)
		}
	}
	if *list {
		fatalIf(json.NewEncoder(os.Stdout).Encode(output{Displays: displays}))
		return
	}
	monitorNumbers, err := parseNumbers(*monitorsText, true)
	if err != nil {
		fatal(err)
	}
	ids := make([]string, len(monitorNumbers))
	for index, number := range monitorNumbers {
		if number > len(displays) {
			fatal(fmt.Errorf("monitor %d is not available", number))
		}
		ids[index] = displays[number-1].ID
	}
	plan, err := windowlayout.Build(displays, windowlayout.Request{
		Mode: *mode, ClientCount: len(ids), DisplayIDs: ids,
		StackedSize: *size, StackedAlign: *align,
	})
	if err != nil {
		fatal(err)
	}
	result := output{Displays: displays, Plan: &plan}
	if *fixture != "" && *apply {
		fatal(errors.New("offline display fixtures cannot be applied"))
	}
	if *pidsText != "" || *apply {
		pids, err := parseNumbers(*pidsText, false)
		if err != nil || len(pids) != len(ids) {
			fatal(errors.New("-pids must list one distinct game process for every client"))
		}
		if *fixture == "" {
			result.Windows, err = windowlayout.DiscoverWindows(pids)
			if err != nil {
				fatal(err)
			}
		} else if *apply {
			fatal(errors.New("offline display fixtures cannot be applied"))
		}
	}
	if *apply {
		if err := windowlayout.Apply(result.Windows, plan); err != nil {
			fatal(err)
		}
		result.Applied = true
	}
	fatalIf(json.NewEncoder(os.Stdout).Encode(result))
}

func fatalIf(err error) {
	if err != nil {
		fatal(err)
	}
}

func fatal(err error) {
	fmt.Fprintln(os.Stderr, err)
	os.Exit(1)
}
