package economy

import (
	"errors"
	"math/big"
	"strings"
)

// Decimal inputs are strings so comparisons retain decimal precision.
// The caller supplies all rule values; this package does not fetch prices or
// claim that a frozen selection manifest is authentic.
type Threshold struct {
	Amount string `json:"amount"`
	Unit   string `json:"unit"`
}

type Rule struct {
	Enabled                 bool       `json:"enabled"`
	SelectedPriceCategories []string   `json:"selected_price_categories"`
	MinimumUnitValue        Threshold  `json:"minimum_unit_value"`
	MinimumTotalStackValue  Threshold  `json:"minimum_total_stack_value"`
	AutoRunMinimumValue     *Threshold `json:"auto_run_minimum_value"`
	EconomyExclusions       []string   `json:"economy_exclusions"`
	ExitHysteresisPercent   string     `json:"exit_hysteresis_percent"`
}

type Quote struct {
	LookupKey     string   `json:"lookup_key"`
	ItemName      string   `json:"item_name"`
	Aliases       []string `json:"aliases"`
	CategoryKey   string   `json:"category_key"`
	DivinePerUnit string   `json:"divine_per_unit"`
}

type Query struct {
	Rule                Rule   `json:"rule"`
	Quote               *Quote `json:"quote"`
	KnownStackQuantity  *int   `json:"known_stack_quantity"`
	ChaosDivinePerUnit  string `json:"chaos_divine_per_unit"`
	WasQualified        bool   `json:"was_qualified"`
	WasAutoRunQualified bool   `json:"was_auto_run_qualified"`
}

type Result struct {
	IsQualified           bool    `json:"is_qualified"`
	IsAutoRunQualified    bool    `json:"is_auto_run_qualified"`
	UnitValueDivine       string  `json:"unit_value_divine"`
	TotalStackValueDivine *string `json:"total_stack_value_divine"`
	Reason                string  `json:"reason"`
	Scope                 string  `json:"scope"`
}

// Evaluate follows the supplied 0.6.17 EconomyInventoryPolicy.Evaluate
// numerical branch. It deliberately excludes manifest construction, hash,
// quote resolution, and player authorization.
func Evaluate(q Query) (Result, error) {
	unit := new(big.Rat)
	if q.Quote != nil {
		var err error
		unit, err = decimal(q.Quote.DivinePerUnit)
		if err != nil {
			return Result{}, err
		}
	}
	chaos, err := decimal(q.ChaosDivinePerUnit)
	if err != nil {
		return Result{}, err
	}
	if chaos.Sign() < 0 {
		chaos = new(big.Rat)
	}
	hysteresis, err := decimal(q.Rule.ExitHysteresisPercent)
	if err != nil {
		return Result{}, err
	}
	hysteresis = clamp(hysteresis, new(big.Rat), big.NewRat(90, 1))
	unitMin, err := toDivine(q.Rule.MinimumUnitValue, chaos)
	if err != nil {
		return Result{}, err
	}
	stackMin, err := toDivine(q.Rule.MinimumTotalStackValue, chaos)
	if err != nil {
		return Result{}, err
	}
	var autoMin *big.Rat
	if q.Rule.AutoRunMinimumValue != nil {
		autoMin, err = toDivine(*q.Rule.AutoRunMinimumValue, chaos)
		if err != nil {
			return Result{}, err
		}
	}
	result := Result{UnitValueDivine: format(maxZero(unit)), Scope: "numerical_policy_only"}
	if !q.Rule.Enabled || q.Quote == nil || unit.Sign() <= 0 {
		if !q.Rule.Enabled {
			result.Reason = "Economy rule is disabled."
		} else {
			result.Reason = "Item is unpriced."
		}
		return result, nil
	}
	if !contains(q.Rule.SelectedPriceCategories, q.Quote.CategoryKey) {
		result.Reason = "Price category is not selected."
		return result, nil
	}
	for _, key := range append([]string{q.Quote.LookupKey, q.Quote.ItemName}, q.Quote.Aliases...) {
		if contains(q.Rule.EconomyExclusions, key) {
			result.Reason = "Item is explicitly excluded."
			return result, nil
		}
	}
	var total *big.Rat
	if q.KnownStackQuantity != nil && *q.KnownStackQuantity >= 0 {
		total = new(big.Rat).Mul(unit, big.NewRat(int64(*q.KnownStackQuantity), 1))
		s := format(total)
		result.TotalStackValueDivine = &s
	}
	retention := new(big.Rat).Sub(big.NewRat(1, 1), new(big.Rat).Quo(hysteresis, big.NewRat(100, 1)))
	unitThreshold := new(big.Rat).Set(unitMin)
	if q.WasQualified {
		unitThreshold.Mul(unitThreshold, retention)
	}
	unitPass := unit.Cmp(unitThreshold) >= 0
	stackPass := total != nil && total.Cmp(stackMin) >= 0
	if !unitPass && !stackPass {
		if total == nil {
			result.Reason = "Unit value is below threshold and stack quantity is unknown."
		} else {
			result.Reason = "Unit and total stack values are below threshold."
		}
		return result, nil
	}
	result.IsQualified = true
	result.IsAutoRunQualified = autoMin == nil
	if autoMin != nil {
		threshold := new(big.Rat).Set(autoMin)
		if q.WasAutoRunQualified {
			threshold.Mul(threshold, retention)
		}
		result.IsAutoRunQualified = unit.Cmp(threshold) >= 0 || (total != nil && total.Cmp(threshold) >= 0)
	}
	if unitPass {
		result.Reason = "Per-unit threshold met."
	} else {
		result.Reason = "Known total stack threshold met."
	}
	return result, nil
}

func decimal(value string) (*big.Rat, error) {
	v := strings.TrimSpace(value)
	if v == "" {
		return nil, errors.New("decimal value is required")
	}
	if strings.ContainsAny(v, "/eE") {
		return nil, errors.New("decimal notation must be plain base-10")
	}
	r := new(big.Rat)
	if _, ok := r.SetString(v); !ok {
		return nil, errors.New("invalid decimal value")
	}
	return r, nil
}

func toDivine(t Threshold, chaos *big.Rat) (*big.Rat, error) {
	amount, err := decimal(t.Amount)
	if err != nil {
		return nil, err
	}
	amount = maxZero(amount)
	switch strings.ToLower(strings.TrimSpace(t.Unit)) {
	case "divine":
		return amount, nil
	case "chaos":
		return new(big.Rat).Mul(amount, chaos), nil
	default:
		return nil, errors.New("threshold unit must be Chaos or Divine")
	}
}

func contains(values []string, target string) bool {
	for _, value := range values {
		if strings.EqualFold(strings.TrimSpace(value), target) {
			return true
		}
	}
	return false
}

func clamp(value, low, high *big.Rat) *big.Rat {
	if value.Cmp(low) < 0 {
		return new(big.Rat).Set(low)
	}
	if value.Cmp(high) > 0 {
		return new(big.Rat).Set(high)
	}
	return value
}

func maxZero(value *big.Rat) *big.Rat {
	if value.Sign() < 0 {
		return new(big.Rat)
	}
	return new(big.Rat).Set(value)
}

func format(value *big.Rat) string {
	s := value.FloatString(28)
	s = strings.TrimRight(strings.TrimRight(s, "0"), ".")
	if s == "" || s == "-" {
		return "0"
	}
	return s
}
