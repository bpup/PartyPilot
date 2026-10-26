package main

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"os"
	"os/signal"
	"strconv"
	"strings"

	"partypilot/internal/resourcepolicy"
)

func parsePIDs(value string) ([]int, error) {
	parts := strings.Split(value, ",")
	if value == "" || len(parts) < 1 || len(parts) > 3 {
		return nil, errors.New("-pids must list 1–3 game process IDs")
	}
	seen := map[int]bool{}
	pids := make([]int, 0, len(parts))
	for _, part := range parts {
		pid, err := strconv.Atoi(part)
		if err != nil || pid <= 0 || seen[pid] {
			return nil, fmt.Errorf("invalid or duplicate process ID %q", part)
		}
		seen[pid] = true
		pids = append(pids, pid)
	}
	return pids, nil
}

type preview struct {
	PIDs    []int                 `json:"pids"`
	Policy  resourcepolicy.Policy `json:"policy"`
	Applied bool                  `json:"applied"`
}

func main() {
	pidsText := flag.String("pids", "", "1–3 explicit game process IDs")
	cpu := flag.Int("cpu", 0, "background hard CPU cap in percent; 0 disables")
	affinityText := flag.String("affinity", "0", "background processor mask, e.g. 0x3; 0 disables")
	ramWarning := flag.Int("ram-warning", 0, "advisory working-set threshold in MB")
	ramLimit := flag.Int("ram-limit", 0, "background hard process memory limit in MB; 0 disables")
	apply := flag.Bool("apply", false, "manage the selected game processes until interrupted")
	controlStdin := flag.Bool("control-stdin", false, "stop and restore when stdin closes or receives stop")
	flag.Parse()
	pids, err := parsePIDs(*pidsText)
	if err != nil {
		fatal(err)
	}
	affinity, err := strconv.ParseUint(*affinityText, 0, 64)
	if err != nil {
		fatal(fmt.Errorf("invalid affinity mask: %w", err))
	}
	policy := resourcepolicy.Policy{CPUPercent: *cpu, AffinityMask: affinity,
		RAMWarningMB: *ramWarning, RAMLimitMB: *ramLimit}
	if err := policy.Validate(); err != nil {
		fatal(err)
	}
	if !*apply {
		if err := json.NewEncoder(os.Stdout).Encode(preview{PIDs: pids, Policy: policy}); err != nil {
			fatal(err)
		}
		return
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()
	if *controlStdin {
		go stopOnControl(os.Stdin, stop)
	}
	if err := resourcepolicy.Run(ctx, pids, policy); err != nil {
		fatal(err)
	}
}

func stopOnControl(input *os.File, stop context.CancelFunc) {
	scanner := bufio.NewScanner(input)
	for scanner.Scan() {
		if strings.TrimSpace(scanner.Text()) == "stop" {
			stop()
			return
		}
	}
	stop() // Parent closed, crashed, or the control stream failed.
}

func fatal(err error) {
	fmt.Fprintln(os.Stderr, err)
	os.Exit(1)
}
