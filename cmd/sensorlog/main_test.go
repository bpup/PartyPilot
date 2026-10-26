package main

import (
	"math"
	"testing"
	"time"
)

func TestSampleValidation(t *testing.T) {
	good := sample{ProcessID: 42, SeenUTC: time.Now(), X: 1, Y: 2, Z: 3}
	if !valid(good) {
		t.Fatal("valid sample rejected")
	}
	good.X = math.NaN()
	if valid(good) {
		t.Fatal("NaN accepted")
	}
}
