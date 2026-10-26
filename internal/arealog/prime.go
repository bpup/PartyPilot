package arealog

import (
	"bufio"
	"io"
	"os"
	"time"
)

const primeBytes int64 = 512 * 1024

// Prime reads a bounded suffix for display only. Historical lines do not
// establish that a game client is still in the recorded instance.
func Prime(path string, slot int, observedAt time.Time) (*Signal, error) {
	file, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer file.Close()
	info, err := file.Stat()
	if err != nil {
		return nil, err
	}
	start := info.Size() - primeBytes
	if start < 0 {
		start = 0
	}
	if _, err := file.Seek(start, io.SeekStart); err != nil {
		return nil, err
	}
	reader := bufio.NewReader(file)
	if start > 0 {
		if _, err := reader.ReadString('\n'); err != nil {
			if err == io.EOF {
				return nil, nil
			}
			return nil, err
		}
	}
	var latest *Signal
	for {
		line, err := reader.ReadString('\n')
		if err == nil {
			if signal, ok := ParseGeneration(line, slot, observedAt); ok {
				signal.Origin = "history"
				latest = &signal
			}
			continue
		}
		if err == io.EOF {
			return latest, nil
		}
		return nil, err
	}
}
