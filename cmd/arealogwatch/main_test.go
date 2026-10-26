package main

import "testing"

func TestLogFlagsRejectDuplicateAndPreserveWindowsPath(t *testing.T) {
	var logs logFlags
	if err := logs.Set(`1=C:\Games\Path of Exile 2\logs\Client.txt`); err != nil || logs[1] != `C:\Games\Path of Exile 2\logs\Client.txt` {
		t.Fatalf("Windows path failed: %v, %q", err, logs[1])
	}
	for _, value := range []string{`1=other.txt`, `0=bad`, `7=bad`, `2`, `2=`} {
		if err := logs.Set(value); err == nil {
			t.Fatalf("accepted invalid -log %q", value)
		}
	}
}
