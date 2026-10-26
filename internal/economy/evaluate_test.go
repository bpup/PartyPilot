package economy

import "testing"

func baseQuery() Query {
	return Query{
		Rule:               Rule{Enabled: true, SelectedPriceCategories: []string{"Currency"}, MinimumUnitValue: Threshold{"10", "Chaos"}, MinimumTotalStackValue: Threshold{"20", "Chaos"}, AutoRunMinimumValue: &Threshold{"30", "Chaos"}, ExitHysteresisPercent: "20"},
		Quote:              &Quote{LookupKey: "item", CategoryKey: "Currency", DivinePerUnit: "0.9"},
		ChaosDivinePerUnit: "0.1",
	}
}

func TestTargetThresholdAndStackCases(t *testing.T) {
	q := baseQuery()
	got, err := Evaluate(q)
	if err != nil || got.IsQualified || got.TotalStackValueDivine != nil {
		t.Fatalf("unknown stack: %+v %v", got, err)
	}
	quantity := 3
	q.KnownStackQuantity = &quantity
	got, err = Evaluate(q)
	if err != nil || !got.IsQualified || got.IsAutoRunQualified || *got.TotalStackValueDivine != "2.7" {
		t.Fatalf("stack 3: %+v %v", got, err)
	}
	quantity = 4
	got, err = Evaluate(q)
	if err != nil || !got.IsAutoRunQualified || *got.TotalStackValueDivine != "3.6" {
		t.Fatalf("stack 4: %+v %v", got, err)
	}
	q.Quote.DivinePerUnit = "1"
	q.KnownStackQuantity = nil
	got, err = Evaluate(q)
	if err != nil || !got.IsQualified || got.IsAutoRunQualified || got.UnitValueDivine != "1" {
		t.Fatalf("unit threshold: %+v %v", got, err)
	}
}

func TestHysteresisAndExclusion(t *testing.T) {
	q := baseQuery()
	q.Rule.MinimumUnitValue = Threshold{"1", "Divine"}
	q.Rule.MinimumTotalStackValue = Threshold{"100", "Divine"}
	q.Quote.DivinePerUnit = "0.85"
	got, _ := Evaluate(q)
	if got.IsQualified {
		t.Fatalf("unlatched: %+v", got)
	}
	q.WasQualified = true
	got, _ = Evaluate(q)
	if !got.IsQualified {
		t.Fatalf("latched: %+v", got)
	}
	q.Quote.DivinePerUnit = "0.79"
	got, _ = Evaluate(q)
	if got.IsQualified {
		t.Fatalf("below exit floor: %+v", got)
	}
	q.Rule.EconomyExclusions = []string{"item"}
	q.Quote.DivinePerUnit = "2"
	got, _ = Evaluate(q)
	if got.IsQualified || got.Reason != "Item is explicitly excluded." {
		t.Fatalf("excluded: %+v", got)
	}
}

func TestCategoryAndInvalidInputs(t *testing.T) {
	q := baseQuery()
	q.Quote.CategoryKey = "Other"
	got, err := Evaluate(q)
	if err != nil || got.IsQualified {
		t.Fatalf("category: %+v %v", got, err)
	}
	q.Rule.MinimumUnitValue.Unit = "Gold"
	if _, err := Evaluate(q); err == nil {
		t.Fatal("unsupported unit accepted")
	}
	q.Rule.MinimumUnitValue.Unit = "Chaos"
	q.Quote.DivinePerUnit = "0.1/2"
	if _, err := Evaluate(q); err == nil {
		t.Fatal("fractional notation accepted")
	}
}
