package windowlayout

// Window styles are 32-bit values even in a 64-bit process.
type windowStyle struct {
	Style   uint32 `json:"style"`
	ExStyle uint32 `json:"exStyle"`
}

type savedStyle struct {
	PID      int         `json:"pid"`
	Handle   uintptr     `json:"handle"`
	Created  uint64      `json:"created"`
	Original windowStyle `json:"original"`
}

const (
	borderMask = 0x00C40000 // caption and sizing frame
	edgeMask   = 0x00020301 // dialog, window, client and static edges
)

func (source windowStyle) borderless() windowStyle {
	return windowStyle{Style: source.Style &^ borderMask, ExStyle: source.ExStyle &^ edgeMask}
}

func (source windowStyle) defaultWindowed() windowStyle {
	return windowStyle{Style: (source.Style &^ 0x80000000) | 0x00CF0000, ExStyle: source.ExStyle | 0x100}
}

func (source windowStyle) isBorderless() bool {
	return source.Style&borderMask == 0 && source.ExStyle&edgeMask == 0
}

func matchingStyle(entries []savedStyle, pid int, handle uintptr, created uint64) (windowStyle, bool) {
	for _, entry := range entries {
		if entry.PID == pid && entry.Handle == handle && entry.Created == created {
			return entry.Original, true
		}
	}
	return windowStyle{}, false
}

func withStyle(entries []savedStyle, next savedStyle) []savedStyle {
	for index := range entries {
		if entries[index].PID == next.PID && entries[index].Handle == next.Handle && entries[index].Created == next.Created {
			entries[index] = next
			return entries
		}
	}
	return append(entries, next)
}
