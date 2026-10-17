namespace Api.Domain;

public enum IncomeKind { Salary, Additional }

// PRD 7.4 income document. An empty ToPeriodKey is open ended; equal to FromPeriodKey is Once (R-INC-03).
public sealed record IncomeItem(
    string Id,
    IncomeKind Kind,
    string Label,
    long AmountMinor,
    string FromPeriodKey,
    string? ToPeriodKey);

// PRD 4.3. Month keys are YYYY-MM, so ordinal comparison follows the calendar.
public static class IncomeCalculator
{
    public const string SalaryLabel = "Salary";

    public static bool IsActive(IncomeItem item, string periodKey) =>
        string.CompareOrdinal(item.FromPeriodKey, periodKey) <= 0 && EndsAtOrAfter(item, periodKey);

    // R-INC-01: the salary active in the month plus every additional item active in the month.
    public static long TotalForPeriod(IEnumerable<IncomeItem> items, string periodKey) =>
        items.Where(item => IsActive(item, periodKey)).Sum(item => item.AmountMinor);

    public static IReadOnlyDictionary<string, long> TotalsForPeriods(IReadOnlyList<IncomeItem> items, IEnumerable<Period> periods) =>
        periods.ToDictionary(period => period.Key, period => TotalForPeriod(items, period.Key));

    // R-INC-02: earlier months keep the old salary. The salary running into the effective month
    // ends the month before, and salary items starting at or after it are replaced (owner's rule, M2).
    public static IReadOnlyList<IncomeItem> ChangeSalary(
        IEnumerable<IncomeItem> items, string newId, long amountMinor, string effectiveFromPeriodKey)
    {
        var previousKey = FiscalCalendar.PreviousKey(effectiveFromPeriodKey);
        var result = new List<IncomeItem>();
        foreach (var item in items)
        {
            if (item.Kind != IncomeKind.Salary)
            {
                result.Add(item);
            }
            else if (string.CompareOrdinal(item.FromPeriodKey, effectiveFromPeriodKey) < 0)
            {
                result.Add(EndsAtOrAfter(item, effectiveFromPeriodKey) ? item with { ToPeriodKey = previousKey } : item);
            }
        }
        result.Add(new IncomeItem(newId, IncomeKind.Salary, SalaryLabel, amountMinor, effectiveFromPeriodKey, null));
        return result;
    }

    private static bool EndsAtOrAfter(IncomeItem item, string periodKey) =>
        string.IsNullOrEmpty(item.ToPeriodKey) || string.CompareOrdinal(periodKey, item.ToPeriodKey) <= 0;
}
