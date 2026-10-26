package main

import (
	"strings"
	"testing"
)

func TestSelectCommandsKeepsManualStop(t *testing.T) {
	input := strings.NewReader("{\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0}}}\n" +
		"{\"commands\":{\"2\":{\"state\":\"holding\",\"reason\":\"manual control\"}}}\n")
	var output strings.Builder
	if err := selectCommands(input, &output, 2); err != nil {
		t.Fatal(err)
	}
	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 2 || !strings.Contains(lines[0], "\"following\"") ||
		!strings.Contains(lines[1], "\"holding\"") {
		t.Fatalf("unexpected output: %q", output.String())
	}
}

func TestAbsentSlotBecomesWaiting(t *testing.T) {
	var output strings.Builder
	if err := selectCommands(strings.NewReader("{\"commands\":{}}\n"), &output, 2); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(output.String(), "\"waiting\"") {
		t.Fatalf("missing slot did not stop: %q", output.String())
	}
}

func TestAllCommandsAndFocus(t *testing.T) {
	input := strings.NewReader("{\"focused\":1,\"commands\":{\"3\":{\"state\":\"following\",\"x\":1,\"y\":0},\"2\":{\"state\":\"waiting\"}}}\n" +
		"{\"focused\":3,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0}}}\n")
	var output strings.Builder
	if err := selectCommands(input, &output, 0); err != nil {
		t.Fatal(err)
	}
	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 5 || !strings.Contains(lines[0], `"type":"focus","slot":1`) ||
		!strings.Contains(lines[1], `"slot":2`) || !strings.Contains(lines[2], `"slot":3`) ||
		!strings.Contains(lines[3], `"type":"focus","slot":3`) || !strings.Contains(lines[4], `"slot":2`) {
		t.Fatalf("unexpected focus or command order: %q", output.String())
	}
}

func TestMappedPartyPlayerUsesRuntimePad(t *testing.T) {
	input := strings.NewReader("{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0}}}\n" +
		"{\"focused\":2,\"commands\":{\"2\":{\"state\":\"waiting\"}}}\n")
	var output strings.Builder
	if err := selectCommandsMapped(input, &output, 0, padMap{1: 1, 2: 3}); err != nil {
		t.Fatal(err)
	}
	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 4 || !strings.Contains(lines[0], `"type":"focus","slot":1`) ||
		!strings.Contains(lines[1], `"slot":3`) ||
		!strings.Contains(lines[2], `"type":"focus","slot":3`) ||
		!strings.Contains(lines[3], `"slot":3,"state":"waiting"`) {
		t.Fatalf("party role did not map to runtime pad: %q", output.String())
	}
}

func TestPadMapRequiresFullUnambiguousMapping(t *testing.T) {
	var mapping padMap
	if err := mapping.Set("1:1"); err != nil {
		t.Fatal(err)
	}
	for _, invalid := range []string{"1:2", "2:1", "0:2", "7:5", "2:7", "x:2"} {
		if err := mapping.Set(invalid); err == nil {
			t.Fatalf("accepted invalid mapping %q", invalid)
		}
	}
	var output strings.Builder
	if err := selectCommandsMapped(strings.NewReader("{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}}}\n"), &output, 0, mapping); err == nil {
		t.Fatal("unmapped party role was forwarded")
	}
	if output.Len() != 0 {
		t.Fatalf("invalid frame emitted a partial focus command: %q", output.String())
	}
}

func TestPrivateRolesRequireExplicitRuntimePads(t *testing.T) {
	input := "{\"focused\":5,\"commands\":{\"4\":{\"state\":\"waiting\"},\"5\":{\"state\":\"following\",\"x\":1,\"y\":0}}}\n"
	var output strings.Builder
	if err := selectCommandsMapped(strings.NewReader(input), &output, 0, nil); err == nil || output.Len() != 0 {
		t.Fatalf("private role crossed without an explicit mapping: %v, %q", err, output.String())
	}
	mapping := padMap{1: 1, 4: 5, 5: 6}
	if err := selectCommandsMapped(strings.NewReader(input), &output, 0, mapping); err != nil {
		t.Fatal(err)
	}
	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 3 || !strings.Contains(lines[0], `"type":"focus","slot":6`) ||
		!strings.Contains(lines[1], `"slot":5,"state":"waiting"`) ||
		!strings.Contains(lines[2], `"slot":6,"state":"following"`) {
		t.Fatalf("private roles did not use explicit runtime pads: %q", output.String())
	}
	var duplicate padMap
	if err := duplicate.Set("4:5"); err != nil {
		t.Fatal(err)
	}
	if err := duplicate.Set("5:5"); err == nil {
		t.Fatal("one private runtime pad was assigned to two party players")
	}
}
