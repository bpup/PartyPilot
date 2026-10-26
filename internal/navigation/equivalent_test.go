package navigation

import (
	"bytes"
	"testing"
)

func TestEquivalentPackedGridRetainsIdentityOnlyForSameContent(t *testing.T) {
	data := bytes.Repeat([]byte{0x11}, 46*92)
	first, err := GridFromPacked("instance", 0, 0, 1, data, 46, 92)
	if err != nil {
		t.Fatal(err)
	}
	second, err := GridFromPacked("instance", 0, 0, 1,
		append([]byte(nil), data...), 46, 92)
	if err != nil || !first.Equivalent(second) {
		t.Fatal("identical packed terrain was treated as a new map")
	}
	second.packed[0] = 0x10
	if first.Equivalent(second) {
		t.Fatal("changed packed terrain reused an old map identity")
	}
}
