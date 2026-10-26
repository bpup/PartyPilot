//go:build !windows

package resourcepolicy

import (
	"context"
	"errors"
)

const Supported = false

func Run(context.Context, []int, Policy) error {
	return errors.New("process resource control is available in the Windows build")
}
