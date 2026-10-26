package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"sort"
	"strconv"
	"strings"

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

type padMap map[int]int

func (m *padMap) String() string { return fmt.Sprint(map[int]int(*m)) }

func (m *padMap) Set(value string) error {
	partyText, padText, ok := strings.Cut(value, ":")
	party, partyError := strconv.Atoi(partyText)
	pad, padError := strconv.Atoi(padText)
	if !ok || partyError != nil || padError != nil || party < 1 || party > 6 || pad < 1 || pad > 4 {
		return fmt.Errorf("pad mapping must be PARTY:PAD with party 1–6 and system pad 1–4")
	}
	if *m == nil {
		*m = make(padMap)
	}
	if _, exists := (*m)[party]; exists {
		return fmt.Errorf("duplicate party player %d", party)
	}
	for _, existing := range *m {
		if existing == pad {
			return fmt.Errorf("system pad %d assigned twice", pad)
		}
	}
	(*m)[party] = pad
	return nil
}

func (m padMap) resolve(party int) (int, error) {
	if len(m) == 0 {
		return party, nil
	}
	pad, ok := m[party]
	if !ok {
		return 0, fmt.Errorf("party player %d has no explicit system pad mapping", party)
	}
	return pad, nil
}

func selectCommands(reader io.Reader, writer io.Writer, slot int) error {
	return selectCommandsMapped(reader, writer, slot, nil)
}

func selectCommandsMapped(reader io.Reader, writer io.Writer, slot int, mapping padMap) error {
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
		if slot == 0 {
			if frame.Focused != 0 {
				if _, err := mapping.resolve(frame.Focused); err != nil {
					return fmt.Errorf("line %d: %w", line, err)
				}
			}
			for party := range frame.Commands {
				if _, err := mapping.resolve(party); err != nil {
					return fmt.Errorf("line %d: %w", line, err)
				}
			}
		} else if _, err := mapping.resolve(slot); err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		if slot == 0 && frame.Focused != 0 && frame.Focused != lastFocus {
			pad, err := mapping.resolve(frame.Focused)
			if err != nil {
				return fmt.Errorf("line %d: %w", line, err)
			}
			if err := encoder.Encode(bridgeFocus{Type: "focus", Slot: pad}); err != nil {
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
				pad, err := mapping.resolve(key)
				if err != nil {
					return fmt.Errorf("line %d: %w", line, err)
				}
				if err := encoder.Encode(bridgeCommand{Slot: pad, Command: frame.Commands[key]}); err != nil {
					return err
				}
			}
			continue
		}
		command, exists := frame.Commands[slot]
		if !exists {
			command = follow.Command{State: follow.Waiting, Reason: "slot absent"}
		}
		pad, err := mapping.resolve(slot)
		if err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		if err := encoder.Encode(bridgeCommand{Slot: pad, Command: command}); err != nil {
			return err
		}
	}
	return scanner.Err()
}

func main() {
	slot := flag.Int("slot", 0, "follower slot to send to one virtual controller")
	all := flag.Bool("all", false, "send every follower command to its slot")
	var mapping padMap
	flag.Var(&mapping, "pad-map", "explicit PARTY:PAD mapping; repeat for every role emitted")
	flag.Parse()
	if (*all && *slot != 0) || (!*all && (*slot < 1 || *slot > 6)) {
		fmt.Fprintln(os.Stderr, "specify -all or one follower slot from 1 to 6")
		os.Exit(2)
	}
	if err := selectCommandsMapped(os.Stdin, os.Stdout, *slot, mapping); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
