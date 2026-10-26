package windowlayout

import "testing"

func TestWindowStyleTransitions(t *testing.T) {
	original := windowStyle{Style: 0x10CF0000, ExStyle: 0x20301}
	borderless := original.borderless()
	if borderless != (windowStyle{Style: 0x100B0000, ExStyle: 0}) || !borderless.isBorderless() {
		t.Fatalf("unexpected borderless style: %+v", borderless)
	}
	if original.isBorderless() {
		t.Fatal("original bordered style was classified as borderless")
	}
	windowed := borderless.defaultWindowed()
	if windowed.Style&0xCF0000 != 0xCF0000 || windowed.ExStyle&0x100 == 0 {
		t.Fatalf("unexpected default windowed style: %+v", windowed)
	}
}

func TestSavedStyleMatchesProcessCreation(t *testing.T) {
	original := windowStyle{Style: 0xCF0000, ExStyle: 0x100}
	entries := withStyle(nil, savedStyle{PID: 12, Handle: 34, Created: 56, Original: original})
	if actual, ok := matchingStyle(entries, 12, 34, 56); !ok || actual != original {
		t.Fatal("saved style not found")
	}
	for _, identity := range [][3]uint64{{13, 34, 56}, {12, 35, 56}, {12, 34, 57}} {
		if _, ok := matchingStyle(entries, int(identity[0]), uintptr(identity[1]), identity[2]); ok {
			t.Fatalf("stale style matched %+v", identity)
		}
	}
}
