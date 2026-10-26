package fpsconfig

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func mustLimit(t *testing.T, input string) Limit {
	t.Helper()
	limit, err := ParseLimit(input)
	if err != nil {
		t.Fatal(err)
	}
	return limit
}

func TestForegroundAndBackgroundAreIndependent(t *testing.T) {
	original := []byte("[DISPLAY]\r\nframerate_limit_enabled=true\r\nframerate_limit=144\r\n" +
		"background_framerate_limit_enabled=false\r\nbackground_framerate_limit=60\r\nunrelated_display_key=keep\r\n")
	policy := Policy{mustLimit(t, "unlimited"), mustLimit(t, "30")}
	patched, keys, err := Patch(original, policy)
	if err != nil || len(keys) != 3 {
		t.Fatalf("FPS keys were not planned independently: %v, %v", keys, err)
	}
	for _, expected := range []string{"framerate_limit_enabled=false", "framerate_limit=144",
		"background_framerate_limit_enabled=true", "background_framerate_limit=30", "unrelated_display_key=keep"} {
		if !bytes.Contains(patched, []byte(expected)) {
			t.Fatalf("%q lost from patch: %s", expected, patched)
		}
	}
	if bytes.Contains(patched, []byte("\n")) && bytes.Contains(bytes.ReplaceAll(patched, []byte("\r\n"), nil), []byte("\n")) {
		t.Fatal("mixed line endings introduced")
	}
	second, changed, err := Patch(patched, policy)
	if err != nil || len(changed) != 0 || !bytes.Equal(second, patched) {
		t.Fatalf("idempotent patch changed config: %v, %v", changed, err)
	}
	keep, changed, err := Patch(original, Policy{mustLimit(t, "keep"), mustLimit(t, "keep")})
	if err != nil || len(changed) != 0 || !bytes.Equal(keep, original) {
		t.Fatal("keep did not preserve current config byte-for-byte")
	}
}

func TestPreservesCommentsEncodingAndUnrelatedSections(t *testing.T) {
	for _, encoding := range []textEncoding{utf8Plain, utf8BOM, utf16LE, utf16BE} {
		input := encode("[GENERAL]\nuser_input_mode=wasd\n[DISPLAY]\n framerate_limit_enabled = false ; own note\n[UI]\ncolor=blue\n", encoding)
		patched, keys, err := Patch(input, Policy{mustLimit(t, "120"), mustLimit(t, "keep")})
		if err != nil || len(keys) != 2 {
			t.Fatalf("encoding %d: %v, %v", encoding, keys, err)
		}
		text, gotEncoding, err := decode(patched)
		if err != nil || gotEncoding != encoding || !strings.Contains(text, "framerate_limit_enabled =true ; own note") ||
			!strings.Contains(text, "framerate_limit=120") || !strings.Contains(text, "user_input_mode=wasd") ||
			!strings.Contains(text, "[UI]\ncolor=blue") {
			t.Fatalf("encoding %d was not preserved: %q, %v", encoding, text, err)
		}
	}
}

func TestRejectsAmbiguousAndInvalidConfig(t *testing.T) {
	policy := Policy{mustLimit(t, "60"), mustLimit(t, "keep")}
	for _, input := range []string{
		"[DISPLAY]\nframerate_limit=100\nframerate_limit=90\n",
		"[DISPLAY]\n[DISPLAY]\n",
		"[DISPLAY]\rframerate_limit=60\r",
	} {
		if _, _, err := Patch([]byte(input), policy); err == nil {
			t.Fatalf("ambiguous config accepted: %q", input)
		}
	}
	if limit := mustLimit(t, "0"); limit.Mode != "unlimited" {
		t.Fatal("zero FPS did not map to the target's unlimited mode")
	}
	for _, value := range []string{"", "-1", "60fps"} {
		if _, err := ParseLimit(value); err == nil {
			t.Fatalf("invalid limit accepted: %q", value)
		}
	}
	if _, _, err := Patch([]byte{0xff}, policy); err == nil {
		t.Fatal("unknown encoding accepted")
	}
	if _, _, err := Patch([]byte{0xff, 0xfe, 0x00, 0xd8}, policy); err == nil {
		t.Fatal("invalid UTF-16 accepted")
	}
}

func TestMissingSectionAndNoFinalNewline(t *testing.T) {
	policy := Policy{mustLimit(t, "keep"), mustLimit(t, "45")}
	for _, original := range []string{"[GENERAL]\nuser_input_mode=wasd", "[DISPLAY]"} {
		patched, keys, err := Patch([]byte(original), policy)
		if err != nil || len(keys) != 2 || !strings.Contains(string(patched),
			"background_framerate_limit_enabled=true\nbackground_framerate_limit=45") ||
			!strings.HasPrefix(string(patched), original) {
			t.Fatalf("missing FPS keys were not inserted safely: %q, %v, %v", patched, keys, err)
		}
	}
}

func TestPreviewDoesNotWriteAndApplyKeepsOriginalBackup(t *testing.T) {
	path := filepath.Join(t.TempDir(), "poe2_production_Config.ini")
	original := []byte("[DISPLAY]\nframerate_limit_enabled=true\nframerate_limit=144\n")
	if err := os.WriteFile(path, original, 0600); err != nil {
		t.Fatal(err)
	}
	policy := Policy{mustLimit(t, "unlimited"), mustLimit(t, "keep")}
	preview, err := Prepare(path, policy, false)
	if err != nil || preview.Applied || preview.Backup != "" || len(preview.ChangedKeys) != 1 {
		t.Fatalf("unexpected preview: %+v, %v", preview, err)
	}
	stillOriginal, err := os.ReadFile(path)
	if err != nil || !bytes.Equal(stillOriginal, original) {
		t.Fatal("preview modified config")
	}
	result, err := Prepare(path, policy, true)
	if err != nil || !result.Applied || result.Backup == "" {
		t.Fatalf("apply failed: %+v, %v", result, err)
	}
	backup, err := os.ReadFile(result.Backup)
	if err != nil || !bytes.Equal(backup, original) {
		t.Fatal("backup differs from original")
	}
	after, err := os.ReadFile(path)
	if err != nil || !bytes.Contains(after, []byte("framerate_limit_enabled=false")) ||
		!bytes.Contains(after, []byte("framerate_limit=144")) {
		t.Fatal("applied FPS setting corrupted config")
	}
}

func TestRefusesSymlinkConfig(t *testing.T) {
	directory := t.TempDir()
	target := filepath.Join(directory, "real.ini")
	link := filepath.Join(directory, "linked.ini")
	if err := os.WriteFile(target, []byte("[DISPLAY]\n"), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(target, link); err != nil {
		t.Skipf("symlink creation unavailable: %v", err)
	}
	if _, err := Prepare(link, Policy{mustLimit(t, "30"), mustLimit(t, "keep")}, true); err == nil {
		t.Fatal("symlink config accepted for replacement")
	}
}
