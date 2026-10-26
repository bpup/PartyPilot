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
