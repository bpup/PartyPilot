package windowlayout

import (
	"reflect"
	"testing"
)

func TestStackedAndTiledPlans(t *testing.T) {
	display := Display{ID: "primary", Work: Rect{Left: 100, Top: 50, Width: 1920, Height: 1080}, Primary: true}
	plan, err := Build([]Display{display}, Request{Mode: "stacked", ClientCount: 2, DisplayIDs: []string{"primary", "primary"}, StackedSize: "small", StackedAlign: "center"})
	if err != nil || plan.Borderless || !reflect.DeepEqual(plan.Placements, []Rect{{Left: 484, Top: 266, Width: 1152, Height: 648}, {Left: 484, Top: 266, Width: 1152, Height: 648}}) {
		t.Fatalf("unexpected stacked plan: %+v, %v", plan, err)
	}
	plan, err = Build([]Display{display}, Request{Mode: "stacked", ClientCount: 1, DisplayIDs: []string{"primary"}, StackedSize: "full", StackedAlign: "left"})
	if err != nil || !plan.Borderless || plan.Placements[0] != display.Work {
		t.Fatalf("unexpected full plan: %+v, %v", plan, err)
	}
	plan, err = Build([]Display{display}, Request{Mode: "tiled", ClientCount: 3, DisplayIDs: []string{"primary", "primary", "primary"}})
	if err != nil || !reflect.DeepEqual(plan.Placements, []Rect{{100, 50, 960, 1080}, {1060, 50, 960, 540}, {1060, 590, 960, 540}}) {
		t.Fatalf("unexpected three-client tile: %+v, %v", plan, err)
	}
}

func TestPortraitAndMultipleDisplays(t *testing.T) {
	portrait := Display{ID: "portrait", Work: Rect{Left: -1080, Top: 0, Width: 1080, Height: 1920}}
	primary := Display{ID: "primary", Work: Rect{Left: 0, Top: 0, Width: 1920, Height: 1080}}
	plan, err := Build([]Display{portrait, primary}, Request{Mode: "tiled", ClientCount: 2, DisplayIDs: []string{"portrait", "portrait"}})
	if err != nil || len(plan.Placements) != 2 || plan.Placements[0].Top >= plan.Placements[1].Top || plan.Placements[0].Width != 1080 {
		t.Fatalf("portrait vertical split failed: %+v, %v", plan, err)
	}
	plan, err = Build([]Display{portrait, primary}, Request{Mode: "multiple", ClientCount: 3, DisplayIDs: []string{"primary", "portrait", "primary"}})
	if err != nil || plan.Placements[1] != portrait.Work || plan.Placements[0].Left != 0 || plan.Placements[2].Left != 960 {
		t.Fatalf("multiple display assignment failed: %+v, %v", plan, err)
	}
	for _, request := range []Request{
		{Mode: "tiled", ClientCount: 2, DisplayIDs: []string{"primary", "unknown"}},
		{Mode: "stacked", ClientCount: 2, DisplayIDs: []string{"primary", "portrait"}, StackedSize: "small", StackedAlign: "center"},
		{Mode: "tiled", ClientCount: 4, DisplayIDs: []string{"primary", "primary", "primary", "primary"}},
	} {
		if _, err := Build([]Display{portrait, primary}, request); err == nil {
			t.Fatalf("accepted invalid layout request: %+v", request)
		}
	}
}
