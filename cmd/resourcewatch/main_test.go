package main

import (
	"context"
	"os"
	"testing"
	"time"
)

func TestParsePIDs(t *testing.T) {
	pids, err := parsePIDs("123,456,789")
	if err != nil || len(pids) != 3 || pids[2] != 789 {
		t.Fatalf("valid PID selection failed: %+v %v", pids, err)
	}
	for _, value := range []string{"", "0", "123,123", "123,a", "1,2,3,4"} {
		if _, err := parsePIDs(value); err == nil {
			t.Fatalf("invalid PID selection accepted: %q", value)
		}
	}
}

func TestControlStdinStopsOnCommandAndParentClose(t *testing.T) {
	for _, command := range []string{"stop\n", ""} {
		reader, writer, err := os.Pipe()
		if err != nil {
			t.Fatal(err)
		}
		ctx, cancel := context.WithCancel(context.Background())
		finished := make(chan struct{})
		go func() {
			stopOnControl(reader, cancel)
			close(finished)
		}()
		if command != "" {
			if _, err := writer.WriteString(command); err != nil {
				t.Fatal(err)
			}
		}
		writer.Close()
		select {
		case <-ctx.Done():
		case <-time.After(time.Second):
			t.Fatal("control input did not stop the resource manager")
		}
		<-finished
		reader.Close()
		cancel()
	}
}
