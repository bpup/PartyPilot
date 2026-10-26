package windowlayout

import (
	"errors"
	"math"
	"strings"
)

type Rect struct {
	Left   int `json:"left"`
	Top    int `json:"top"`
	Width  int `json:"width"`
	Height int `json:"height"`
}

type Display struct {
	ID      string `json:"id"`
	Work    Rect   `json:"work"`
	Primary bool   `json:"primary"`
}

type Request struct {
	Mode         string
	ClientCount  int
	DisplayIDs   []string
	StackedSize  string
	StackedAlign string
}

type Plan struct {
	Placements []Rect `json:"placements"`
	Borderless bool   `json:"borderless"`
}

func Build(displays []Display, request Request) (Plan, error) {
	if request.ClientCount < 1 || request.ClientCount > 3 || len(request.DisplayIDs) != request.ClientCount {
		return Plan{}, errors.New("layout needs 1–3 clients and one display assignment per client")
	}
	lookup := make(map[string]Display, len(displays))
	for _, display := range displays {
		if display.ID == "" || display.Work.Width < 320 || display.Work.Height < 240 {
			return Plan{}, errors.New("invalid display work area")
		}
		key := strings.ToLower(display.ID)
		if _, exists := lookup[key]; exists {
			return Plan{}, errors.New("duplicate display ID")
		}
		lookup[key] = display
	}
	assigned := make([]Display, request.ClientCount)
	for index, id := range request.DisplayIDs {
		display, exists := lookup[strings.ToLower(id)]
		if !exists {
			return Plan{}, errors.New("assigned display was not found")
		}
		assigned[index] = display
	}
	switch request.Mode {
	case "stacked":
		for _, display := range assigned[1:] {
			if !strings.EqualFold(display.ID, assigned[0].ID) {
				return Plan{}, errors.New("stacked clients need one display")
			}
		}
		placement, borderless, err := stacked(assigned[0].Work, request.StackedSize, request.StackedAlign)
		if err != nil {
			return Plan{}, err
		}
		plan := Plan{Borderless: borderless, Placements: make([]Rect, request.ClientCount)}
		for index := range plan.Placements {
			plan.Placements[index] = placement
		}
		return plan, nil
	case "tiled":
		for _, display := range assigned[1:] {
			if !strings.EqualFold(display.ID, assigned[0].ID) {
				return Plan{}, errors.New("tiled clients need one display")
			}
		}
		return Plan{Placements: tile(assigned[0].Work, request.ClientCount)}, nil
	case "multiple":
		plan := Plan{Placements: make([]Rect, request.ClientCount)}
		groups := make(map[string][]int)
		for index, display := range assigned {
			groups[strings.ToLower(display.ID)] = append(groups[strings.ToLower(display.ID)], index)
		}
		for _, indexes := range groups {
			work := assigned[indexes[0]].Work
			var placements []Rect
			if verticalPreferred(work, len(indexes)) {
				placements = vertical(work, len(indexes))
			} else {
				placements = split(work, len(indexes), true)
			}
			for position, index := range indexes {
				plan.Placements[index] = placements[position]
			}
		}
		return plan, nil
	default:
		return Plan{}, errors.New("layout mode must be stacked, tiled, or multiple")
	}
}

func stacked(work Rect, size, align string) (Rect, bool, error) {
	percent := 0
	borderless := false
	switch size {
	case "small":
		percent = 60
	case "medium":
		percent = 75
	case "large":
		percent = 90
	case "full":
		percent, borderless = 100, true
	default:
		return Rect{}, false, errors.New("stacked size must be small, medium, large, or full")
	}
	if align != "left" && align != "center" && align != "right" {
		return Rect{}, false, errors.New("stacked alignment must be left, center, or right")
	}
	width, height := work.Width, work.Height
	if !borderless {
		width = min(work.Width, max(320, work.Width*percent/100))
		height = min(work.Height, max(240, work.Height*percent/100))
		width, height = fitSixteenNine(width, height)
	}
	left := work.Left
	if align == "center" {
		left += (work.Width - width) / 2
	} else if align == "right" {
		left += work.Width - width
	}
	return Rect{Left: left, Top: work.Top + (work.Height-height)/2, Width: width, Height: height}, borderless, nil
}

func tile(work Rect, count int) []Rect {
	if count == 1 {
		return []Rect{work}
	}
	if verticalPreferred(work, count) {
		return vertical(work, count)
	}
	if count == 2 || float64(work.Width)/float64(work.Height) >= 2.1 {
		return split(work, count, true)
	}
	middleX := work.Left + work.Width/2
	middleY := work.Top + work.Height/2
	return []Rect{
		{Left: work.Left, Top: work.Top, Width: middleX - work.Left, Height: work.Height},
		{Left: middleX, Top: work.Top, Width: work.Left + work.Width - middleX, Height: middleY - work.Top},
		{Left: middleX, Top: middleY, Width: work.Left + work.Width - middleX, Height: work.Top + work.Height - middleY},
	}
}

func split(work Rect, count int, sideBySide bool) []Rect {
	result := make([]Rect, count)
	for index := 0; index < count; index++ {
		if sideBySide {
			left := work.Left + work.Width*index/count
			right := work.Left + work.Width*(index+1)/count
			result[index] = Rect{Left: left, Top: work.Top, Width: right - left, Height: work.Height}
		} else {
			top := work.Top + work.Height*index/count
			bottom := work.Top + work.Height*(index+1)/count
			result[index] = Rect{Left: work.Left, Top: top, Width: work.Width, Height: bottom - top}
		}
	}
	return result
}

func vertical(work Rect, count int) []Rect {
	result := make([]Rect, count)
	for index := 0; index < count; index++ {
		top := work.Top + work.Height*index/count
		bottom := work.Top + work.Height*(index+1)/count
		width, height := fitSixteenNine(work.Width, bottom-top)
		result[index] = Rect{Left: work.Left + (work.Width-width)/2, Top: top + (bottom-top-height)/2, Width: width, Height: height}
	}
	return result
}

func verticalPreferred(work Rect, count int) bool {
	if count <= 1 {
		return false
	}
	aspect := float64(work.Width) / float64(work.Height)
	preferred := 16.0 / 9.0
	verticalDistortion := math.Abs(math.Log((aspect * float64(count)) / preferred))
	horizontalDistortion := math.Abs(math.Log((aspect / float64(count)) / preferred))
	return verticalDistortion+0.01 < horizontalDistortion
}

func fitSixteenNine(width, height int) (int, int) {
	fitHeight := max(1, int(math.Floor(float64(width)*9/16+0.5)))
	if fitHeight <= height {
		return width, fitHeight
	}
	fitWidth := max(1, int(math.Floor(float64(height)*16/9+0.5)))
	return min(width, fitWidth), height
}
