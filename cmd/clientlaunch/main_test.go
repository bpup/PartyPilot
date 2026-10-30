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

func TestPrivateClientPreResumeHandshake(t *testing.T) {
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
	defer func() {
		os.Stdin, os.Stdout = oldInput, oldOutput
		inputRead.Close()
		inputWrite.Close()
		outputRead.Close()
		outputWrite.Close()
	}()
	done := make(chan error, 1)
	go func() {
		if err := waitForPrivateClientPreparation(1, 101, `C:\PoE2\PathOfExile.exe`); err != nil {
			done <- err
			return
		}
		done <- waitForPrivateClientPreparation(2, 102, `C:\PoE2\PathOfExile_x64.exe`)
	}()
	reader := bufio.NewReader(outputRead)
	for client := 1; client <= 2; client++ {
		line, err := reader.ReadBytes('\n')
		if err != nil {
			t.Fatal(err)
		}
		var notice struct {
			Event      string `json:"event"`
			Client     int    `json:"client"`
			PID        int    `json:"pid"`
			Executable string `json:"executable"`
		}
		if err := json.Unmarshal(line, &notice); err != nil ||
			notice.Event != "private_client_pre_resume" || notice.Client != client ||
			notice.PID != 100+client || notice.Executable == "" {
			t.Fatalf("wrong private client notice: %+v %v", notice, err)
		}
		if _, err := fmt.Fprintf(inputWrite, "resume %d %d\n", client, notice.PID); err != nil {
			t.Fatal(err)
		}
	}
	if err := <-done; err != nil {
		t.Fatal(err)
	}
	go func() { done <- waitForPrivateClientPreparation(3, 103, `C:\PoE2\PathOfExile.exe`) }()
	if _, err := reader.ReadBytes('\n'); err != nil {
		t.Fatal(err)
	}
	if _, err := fmt.Fprintln(inputWrite, "resume 2 103"); err != nil {
		t.Fatal(err)
	}
	if err := <-done; err == nil {
		t.Fatal("accepted an acknowledgement for a different client")
	}
}

func TestParsePreResumeClients(t *testing.T) {
	selected, err := parsePreResumeClients("1,2,3", 3)
	if err != nil || len(selected) != 3 || selected[0] != 1 || selected[2] != 3 {
		t.Fatalf("valid private client list rejected: %v %v", selected, err)
	}
	for _, raw := range []string{"0", "4", "1,1", "1,", "1, 2", "x"} {
		if _, err := parsePreResumeClients(raw, 3); err == nil {
			t.Fatalf("accepted invalid private client list %q", raw)
		}
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
