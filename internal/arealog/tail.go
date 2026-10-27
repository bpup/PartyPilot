package arealog

import (
	"bytes"
	"fmt"
	"io"
	"os"
)

// Tail begins at EOF so old log entries cannot be presented as fresh evidence.
type Tail struct {
	path     string
	info     os.FileInfo
	position int64
	partial  []byte
	closed   bool
}

func OpenTail(path string) (*Tail, error) {
	file, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	info, err := file.Stat()
	if err != nil || !info.Mode().IsRegular() {
		file.Close()
		return nil, fmt.Errorf("log is not a regular file")
	}
	position, err := file.Seek(0, io.SeekEnd)
	if err != nil {
		file.Close()
		return nil, err
	}
	if err := file.Close(); err != nil {
		return nil, err
	}
	return &Tail{path: path, info: info, position: position}, nil
}

func (t *Tail) Close() error {
	t.closed = true
	return nil
}

func (t *Tail) Poll() ([]string, error) {
	if t.closed {
		return nil, os.ErrClosed
	}
	file, err := os.Open(t.path)
	if err != nil {
		return nil, err
	}
	defer file.Close()
	info, err := file.Stat()
	if err != nil {
		return nil, err
	}
	if !info.Mode().IsRegular() {
		return nil, fmt.Errorf("log is not a regular file")
	}
	if !os.SameFile(info, t.info) || info.Size() < t.position {
		t.info, t.position, t.partial = info, 0, nil
	}
	if _, err := file.Seek(t.position, io.SeekStart); err != nil {
		return nil, err
	}
	var lines []string
	buffer := make([]byte, 4096)
	for {
		count, err := file.Read(buffer)
		if count > 0 {
			t.position += int64(count)
			t.partial = append(t.partial, buffer[:count]...)
			for {
				end := bytes.IndexByte(t.partial, '\n')
				if end < 0 {
					break
				}
				lines = append(lines, string(bytes.TrimSuffix(t.partial[:end], []byte{'\r'})))
				t.partial = t.partial[end+1:]
			}
			if len(t.partial) > 64*1024 {
				return nil, fmt.Errorf("log line exceeds 64 KiB")
			}
		}
		if err == io.EOF {
			return lines, nil
		}
		if err != nil {
			return nil, err
		}
	}
}
