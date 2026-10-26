package combat

import "testing"

func TestStablePacingHashUsesSigned32BitOverflow(t *testing.T) {
	if got := stablePacingHash("enc-100:P4"); got != 931755215 {
		t.Fatalf("C# stable hash overflow differs: %d", got)
	}
}
