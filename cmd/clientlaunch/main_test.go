package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"testing"

	"partypilot/internal/windowlayout"
)

func TestClient3PreResumeHandshake(t *testing.T) {
	oldInput, oldOutput := os.Stdin, os.Stdout
	inputRead, inputWrite, err := os.Pipe()
	if err != nil {
		t.Fatal(err)
	}
	outputRead, outputWrite, err := os.Pipe()
	if err != nil {
		t.Fatal(err)
	}
	os.Stdin, os.Stdout = inputRead, outputWrite
	t.Cleanup(func() {
		os.Stdin, os.Stdout = oldInput, oldOutput
		inputRead.Close()
		inputWrite.Close()
		outputRead.Close()
		outputWrite.Close()
	})
	done := make(chan error, 1)
	go func() { done <- waitForClient3Preparation(123, `C:\PoE2\PathOfExile_x64.exe`) }()
	line, err := bufio.NewReader(outputRead).ReadBytes('\n')
	if err != nil {
		t.Fatal(err)
	}
	var notice struct {
		Event      string `json:"event"`
		PID        int    `json:"pid"`
		Executable string `json:"executable"`
	}
	if err := json.Unmarshal(line, &notice); err != nil || notice.Event != "client3_pre_resume" ||
		notice.PID != 123 || notice.Executable != `C:\PoE2\PathOfExile_x64.exe` {
		t.Fatalf("wrong pre-resume notice: %+v %v", notice, err)
	}
	if _, err := fmt.Fprintln(inputWrite, "resume 123"); err != nil {
		t.Fatal(err)
	}
	if err := <-done; err != nil {
		t.Fatal(err)
	}
}

func TestMonitorAssignments(t *testing.T) {
	displays := []windowlayout.Display{{ID: "monitor-1"}, {ID: "monitor-2"}}
	ids, err := monitorIDs("1,2,1", displays, 3)
	if err != nil || len(ids) != 3 || ids[1] != "monitor-2" {
		t.Fatalf("valid assignments failed: %v, %v", ids, err)
	}
	for _, value := range []string{"", "1", "0,2", "1,3", "a,2"} {
		if _, err := monitorIDs(value, displays, 2); err == nil {
			t.Fatalf("accepted invalid display assignment %q", value)
		}
	}
}
