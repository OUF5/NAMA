namespace Api.Domain;

// PRD 7.4 plan document: the planned amounts of one sub-category in one FY, by month key.
// Only amounts above 0 are stored.
public sealed record PlanAmounts(string SubCategoryId, string CategoryId, IReadOnlyDictionary<string, long> Amounts);

// PRD 4.4. Amounts are long minor units (R-MON-01).
public static class PlanCalculator
{
    // R-PLN-01: a missing amount counts as 0.
    public static long AmountFor(PlanAmounts plan, string periodKey) => plan.Amounts.GetValueOrDefault(periodKey);

    // R-PLN-02: month planned = sum of all sub-categories.
    public static long MonthPlanned(IEnumerable<PlanAmounts> plans, string periodKey) =>
        plans.Sum(plan => AmountFor(plan, periodKey));

    // R-PLN-02: category planned = sum of its sub-categories.
    public static long CategoryPlanned(IEnumerable<PlanAmounts> plans, string categoryId, string periodKey) =>
        MonthPlanned(plans.Where(plan => plan.CategoryId == categoryId), periodKey);

    // The FY total of one sub-category: only the FY's own months count.
    public static long Total(PlanAmounts plan, IEnumerable<Period> periods) =>
        periods.Sum(period => AmountFor(plan, period.Key));

    // R-PLN-03: total income minus total planned; negative means over-planned.
    public static long Unplanned(long income, long planned) => income - planned;

    // ENT-01: planned minus spent; negative means over plan.
    public static long Remaining(long planned, long spent) => planned - spent;

    // DSH-02: income minus spent; negative means more was spent than earned.
    public static long Saved(long income, long spent) => income - spent;

    // ENT-01: share of the plan spent, rounded down (79.99% is 79). Null when nothing is planned.
    public static int? Percent(long planned, long spent) =>
        planned <= 0 ? null : (int)Math.Min(spent * 100 / planned, int.MaxValue);

    // FY-02 (owner's choice, M8): each new month gets the amount of the same month a year earlier,
    // from whichever FY holds it. Only active sub-categories are copied (an archived one would be
    // hidden but still counted). Sub-categories with nothing to copy get no plan.
    public static IReadOnlyList<PlanAmounts> CopyForNextFiscalYear(
        IEnumerable<PlanAmounts> sourcePlans, IEnumerable<Period> newPeriods, IReadOnlySet<string> activeSubCategoryIds)
    {
        var keys = newPeriods.Select(period => period.Key).ToList();
        return sourcePlans
            .Where(plan => activeSubCategoryIds.Contains(plan.SubCategoryId))
            .GroupBy(plan => plan.SubCategoryId)
            .Select(group =>
            {
                var amounts = keys
                    .Select(key => (Key: key, Amount: group.Sum(plan => AmountFor(plan, FiscalCalendar.YearEarlierKey(key)))))
                    .Where(item => item.Amount > 0)
                    .ToDictionary(item => item.Key, item => item.Amount);
                return new PlanAmounts(group.Key, group.First().CategoryId, amounts);
            })
            .Where(plan => plan.Amounts.Count > 0)
            .ToList();
    }

    // Owner's choice (M4): 0 clears a month, so it is dropped (stored amounts stay above 0, R-MON-02).
    // Returns null when a key is not one of the FY's stored months (R-CAL-09).
    public static IReadOnlyDictionary<string, long>? NormalizeAmounts(
        IReadOnlyDictionary<string, long> amounts, IEnumerable<Period> periods)
    {
        var keys = periods.Select(period => period.Key).ToHashSet(StringComparer.Ordinal);
        if (!amounts.Keys.All(keys.Contains))
        {
            return null;
        }
        return amounts.Where(pair => pair.Value != 0).ToDictionary(pair => pair.Key, pair => pair.Value);
    }
}
