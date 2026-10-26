package fpsconfig

import (
	"bytes"
	"encoding/binary"
	"errors"
	"fmt"
	"strconv"
	"strings"
	"unicode/utf16"
	"unicode/utf8"
)

type Limit struct {
	Mode string `json:"mode"`
	FPS  int    `json:"fps,omitempty"`
}

func ParseLimit(value string) (Limit, error) {
	switch strings.ToLower(strings.TrimSpace(value)) {
	case "keep":
		return Limit{Mode: "keep"}, nil
	case "unlimited", "0":
		return Limit{Mode: "unlimited"}, nil
	}
	fps, err := strconv.Atoi(strings.TrimSpace(value))
	if err != nil || fps <= 0 {
		return Limit{}, errors.New("FPS must be keep, unlimited, or a positive integer")
	}
	return Limit{Mode: "limited", FPS: fps}, nil
}

type Policy struct {
	Foreground Limit `json:"foreground"`
	Background Limit `json:"background"`
}

func (policy Policy) Validate() error {
	for _, limit := range []Limit{policy.Foreground, policy.Background} {
		if !((limit.Mode == "keep" || limit.Mode == "unlimited") && limit.FPS == 0 ||
			limit.Mode == "limited" && limit.FPS > 0) {
			return errors.New("invalid FPS policy")
		}
	}
	return nil
}

// Patch changes only the requested DISPLAY FPS keys. Ambiguous duplicate keys
// or sections are rejected instead of silently choosing one occurrence.
func Patch(input []byte, policy Policy) ([]byte, []string, error) {
	if err := policy.Validate(); err != nil {
		return nil, nil, err
	}
	text, encoding, err := decode(input)
	if err != nil {
		return nil, nil, err
	}
	if policy.Foreground.Mode == "keep" && policy.Background.Mode == "keep" {
		return bytes.Clone(input), nil, nil
	}
	if strings.Contains(strings.ReplaceAll(text, "\r\n", ""), "\r") {
		return nil, nil, errors.New("unsupported lone-CR line endings")
	}
	newline := "\n"
	if strings.Contains(text, "\r\n") {
		newline = "\r\n"
	}
	lines := splitLines(text)
	sectionStart, sectionEnd := -1, len(lines)
	current := ""
	for index, line := range lines {
		body, _ := splitEnd(line)
		trimmed := strings.TrimSpace(body)
		if len(trimmed) > 2 && strings.HasPrefix(trimmed, "[") && strings.HasSuffix(trimmed, "]") {
			if current == "DISPLAY" && sectionEnd == len(lines) {
				sectionEnd = index
			}
			current = strings.ToUpper(strings.TrimSpace(trimmed[1 : len(trimmed)-1]))
			if current == "DISPLAY" {
				if sectionStart >= 0 {
					return nil, nil, errors.New("duplicate DISPLAY sections")
				}
				sectionStart = index
			}
		}
	}
	if sectionStart < 0 {
		if len(lines) > 0 {
			_, ending := splitEnd(lines[len(lines)-1])
			if ending == "" {
				lines[len(lines)-1] += newline
			}
		}
		sectionStart = len(lines)
		lines = append(lines, "[DISPLAY]"+newline)
		sectionEnd = len(lines)
	}
	settings := make([]struct{ key, value string }, 0, 4)
	add := func(enabled, amount string, limit Limit) {
		switch limit.Mode {
		case "unlimited":
			settings = append(settings, struct{ key, value string }{enabled, "false"})
		case "limited":
			settings = append(settings, struct{ key, value string }{enabled, "true"},
				struct{ key, value string }{amount, strconv.Itoa(limit.FPS)})
		}
	}
	add("framerate_limit_enabled", "framerate_limit", policy.Foreground)
	add("background_framerate_limit_enabled", "background_framerate_limit", policy.Background)
	changed := make([]string, 0, len(settings))
	for _, setting := range settings {
		match := -1
		for index := sectionStart + 1; index < sectionEnd; index++ {
			body, _ := splitEnd(lines[index])
			trimmed := strings.TrimSpace(body)
			if trimmed == "" || strings.HasPrefix(trimmed, ";") || strings.HasPrefix(trimmed, "#") {
				continue
			}
			key, _, ok := strings.Cut(body, "=")
			if ok && strings.EqualFold(strings.TrimSpace(key), setting.key) {
				if match >= 0 {
					return nil, nil, fmt.Errorf("duplicate DISPLAY.%s keys", setting.key)
				}
				match = index
			}
		}
		if match < 0 {
			if sectionEnd > 0 {
				_, ending := splitEnd(lines[sectionEnd-1])
				if ending == "" {
					lines[sectionEnd-1] += newline
				}
			}
			lines = append(lines[:sectionEnd], append([]string{setting.key + "=" + setting.value + newline}, lines[sectionEnd:]...)...)
			sectionEnd++
			changed = append(changed, "DISPLAY."+setting.key)
			continue
		}
		body, ending := splitEnd(lines[match])
		equals := strings.IndexByte(body, '=')
		valueStart := equals + 1
		commentStart := len(body)
		for index := valueStart; index < len(body); index++ {
			if (body[index] == ';' || body[index] == '#') &&
				(index == valueStart || body[index-1] == ' ' || body[index-1] == '\t') {
				commentStart = index
				break
			}
		}
		if strings.EqualFold(strings.TrimSpace(body[valueStart:commentStart]), setting.value) {
			continue
		}
		comment := body[commentStart:]
		if comment != "" && !strings.HasPrefix(comment, " ") && !strings.HasPrefix(comment, "\t") {
			comment = " " + comment
		}
		lines[match] = body[:valueStart] + setting.value + comment + ending
		changed = append(changed, "DISPLAY."+setting.key)
	}
	if len(changed) == 0 {
		return bytes.Clone(input), nil, nil
	}
	return encode(strings.Join(lines, ""), encoding), changed, nil
}

func splitLines(text string) []string {
	if text == "" {
		return nil
	}
	parts := strings.SplitAfter(text, "\n")
	if parts[len(parts)-1] == "" {
		parts = parts[:len(parts)-1]
	}
	return parts
}

func splitEnd(line string) (string, string) {
	if strings.HasSuffix(line, "\r\n") {
		return strings.TrimSuffix(line, "\r\n"), "\r\n"
	}
	if strings.HasSuffix(line, "\n") {
		return strings.TrimSuffix(line, "\n"), "\n"
	}
	return line, ""
}

type textEncoding int

const (
	utf8Plain textEncoding = iota
	utf8BOM
	utf16LE
	utf16BE
)

func decode(data []byte) (string, textEncoding, error) {
	if bytes.HasPrefix(data, []byte{0xef, 0xbb, 0xbf}) {
		if !utf8.Valid(data[3:]) {
			return "", 0, errors.New("invalid UTF-8 config")
		}
		return string(data[3:]), utf8BOM, nil
	}
	for _, candidate := range []struct {
		bom   []byte
		order binary.ByteOrder
		kind  textEncoding
	}{{[]byte{0xff, 0xfe}, binary.LittleEndian, utf16LE}, {[]byte{0xfe, 0xff}, binary.BigEndian, utf16BE}} {
		if bytes.HasPrefix(data, candidate.bom) {
			body := data[2:]
			if len(body)%2 != 0 {
				return "", 0, errors.New("odd UTF-16 config length")
			}
			units := make([]uint16, len(body)/2)
			for index := range units {
				units[index] = candidate.order.Uint16(body[index*2:])
			}
			decoded := string(utf16.Decode(units))
			if !bytes.Equal(encode(decoded, candidate.kind), data) {
				return "", 0, errors.New("invalid UTF-16 config")
			}
			return decoded, candidate.kind, nil
		}
	}
	if !utf8.Valid(data) {
		return "", 0, errors.New("unknown config encoding")
	}
	return string(data), utf8Plain, nil
}

func encode(text string, kind textEncoding) []byte {
	switch kind {
	case utf8BOM:
		return append([]byte{0xef, 0xbb, 0xbf}, []byte(text)...)
	case utf16LE, utf16BE:
		units := utf16.Encode([]rune(text))
		result := make([]byte, 2+len(units)*2)
		order := binary.ByteOrder(binary.LittleEndian)
		if kind == utf16BE {
			order = binary.BigEndian
		}
		order.PutUint16(result[:2], 0xfeff)
		for index, unit := range units {
			order.PutUint16(result[2+index*2:], unit)
		}
		return result
	default:
		return []byte(text)
	}
}
