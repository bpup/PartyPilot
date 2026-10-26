package arealog

import (
	"bytes"
	"fmt"
	"io"
	"os"
)

// Tail begins at EOF so old log entries cannot be presented as fresh evidence.
type Tail struct {
	path    string
	file    *os.File
	info    os.FileInfo
	partial []byte
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
	if _, err := file.Seek(0, io.SeekEnd); err != nil {
		file.Close()
		return nil, err
	}
	return &Tail{path: path, file: file, info: info}, nil
}

func (t *Tail) Close() error { return t.file.Close() }

func (t *Tail) Poll() ([]string, error) {
	info, err := os.Stat(t.path)
	if err != nil {
		return nil, err
	}
	if !info.Mode().IsRegular() {
		return nil, fmt.Errorf("log is not a regular file")
	}
	position, err := t.file.Seek(0, io.SeekCurrent)
	if err != nil {
		return nil, err
	}
	if !os.SameFile(info, t.info) || info.Size() < position {
		file, err := os.Open(t.path)
		if err != nil {
			return nil, err
		}
		if err := t.file.Close(); err != nil {
			file.Close()
			return nil, err
		}
		t.file, t.info, t.partial = file, info, nil
	}
	var lines []string
	buffer := make([]byte, 4096)
	for {
		count, err := t.file.Read(buffer)
		if count > 0 {
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
