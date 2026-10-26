package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"sort"

	"partypilot/internal/follow"
)

type partyFrame struct {
	Focused  int                    `json:"focused"`
	Commands map[int]follow.Command `json:"commands"`
}

type bridgeFocus struct {
	Type string `json:"type"`
	Slot int    `json:"slot"`
}

type bridgeCommand struct {
	Slot int `json:"slot"`
	follow.Command
}

func selectCommands(reader io.Reader, writer io.Writer, slot int) error {
	scanner := bufio.NewScanner(reader)
	scanner.Buffer(make([]byte, 64*1024), 1024*1024)
	encoder := json.NewEncoder(writer)
	line := 0
	lastFocus := 0
	for scanner.Scan() {
		line++
		var frame partyFrame
		if err := json.Unmarshal(scanner.Bytes(), &frame); err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		if slot == 0 && frame.Focused != 0 && frame.Focused != lastFocus {
			if err := encoder.Encode(bridgeFocus{Type: "focus", Slot: frame.Focused}); err != nil {
				return err
			}
			lastFocus = frame.Focused
		}
		if slot == 0 {
			keys := make([]int, 0, len(frame.Commands))
			for key := range frame.Commands {
				keys = append(keys, key)
			}
			sort.Ints(keys)
			for _, key := range keys {
				if err := encoder.Encode(bridgeCommand{Slot: key, Command: frame.Commands[key]}); err != nil {
					return err
				}
			}
			continue
		}
		command, exists := frame.Commands[slot]
		if !exists {
			command = follow.Command{State: follow.Waiting, Reason: "slot absent"}
		}
		if err := encoder.Encode(bridgeCommand{Slot: slot, Command: command}); err != nil {
			return err
		}
	}
	return scanner.Err()
}

func main() {
	slot := flag.Int("slot", 0, "follower slot to send to one virtual controller")
	all := flag.Bool("all", false, "send every follower command to its slot")
	flag.Parse()
	if (*all && *slot != 0) || (!*all && (*slot < 1 || *slot > 6)) {
		fmt.Fprintln(os.Stderr, "specify -all or one follower slot from 1 to 6")
		os.Exit(2)
	}
	if err := selectCommands(os.Stdin, os.Stdout, *slot); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
