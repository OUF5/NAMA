namespace Api.Domain;

public enum IncomeKind { Salary, Additional }

// R-INC-04 (owner note 17): what an item counts in. Both is the default, so items saved before it
// existed keep their numbers.
public enum IncomeCounts { Both, Planned, Actual }

// PRD 7.4 income document. An empty ToPeriodKey is open ended; equal to FromPeriodKey is Once (R-INC-03).
// BankAccountId (R-INC-05): the account an Actual or Both item goes into on payday; null goes through
// the income split.
public sealed record IncomeItem(
    string Id,
    IncomeKind Kind,
    string Label,
    long AmountMinor,
    string FromPeriodKey,
    string? ToPeriodKey,
    IncomeCounts Counts = IncomeCounts.Both,
    string? BankAccountId = null)
{
    public bool InPlan => Counts != IncomeCounts.Actual;

    public bool InActual => Counts != IncomeCounts.Planned;
}

// R-INC-01, R-INC-05 (owner note 17): one user's income, by month key, read once. Planned is Plan's;
// Actual is Tracker's, Dashboard's and the balances'. Of the actual income, Split is what the income
// split shares out on payday (R-BAL-02) and Direct (account id → month key → amount) what goes into
// one account.
public sealed record IncomeTotals(
    IReadOnlyDictionary<string, long> Planned,
    IReadOnlyDictionary<string, long> Actual,
    IReadOnlyDictionary<string, long> Split,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Direct);

// PRD 4.3. Month keys are YYYY-MM, so ordinal comparison follows the calendar.
public static class IncomeCalculator
{
    public const string SalaryLabel = "Salary";

    public static bool IsActive(IncomeItem item, string periodKey) =>
        string.CompareOrdinal(item.FromPeriodKey, periodKey) <= 0 && EndsAtOrAfter(item, periodKey);

    // R-INC-01: the salary active in the month plus every additional item active in the month that
    // counts in the plan (R-INC-04). Plan uses it.
    public static long PlannedForPeriod(IEnumerable<IncomeItem> items, string periodKey) =>
        Sum(items.Where(item => item.InPlan), periodKey);

    // R-INC-01: the same for the actuals. Tracker, Dashboard's FY to date and the balances use it.
    public static long ActualForPeriod(IEnumerable<IncomeItem> items, string periodKey) =>
        Sum(items.Where(item => item.InActual), periodKey);

    public static IReadOnlyDictionary<string, long> PlannedTotals(IReadOnlyList<IncomeItem> items, IEnumerable<Period> periods) =>
        periods.ToDictionary(period => period.Key, period => PlannedForPeriod(items, period.Key));

    public static IReadOnlyDictionary<string, long> ActualTotals(IReadOnlyList<IncomeItem> items, IEnumerable<Period> periods) =>
        periods.ToDictionary(period => period.Key, period => ActualForPeriod(items, period.Key));

    public static IncomeTotals Totals(IReadOnlyList<IncomeItem> items, IReadOnlyList<Period> periods) => new(
        PlannedTotals(items, periods),
        ActualTotals(items, periods),
        periods.ToDictionary(period => period.Key, period => Sum(items.Where(item => item.InActual && item.BankAccountId is null), period.Key)),
        items.Where(item => item.InActual && item.BankAccountId is not null)
            .GroupBy(item => item.BankAccountId!)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, long>)periods.ToDictionary(period => period.Key, period => Sum(group, period.Key))));

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

    private static long Sum(IEnumerable<IncomeItem> items, string periodKey) =>
        items.Where(item => IsActive(item, periodKey)).Sum(item => item.AmountMinor);

    private static bool EndsAtOrAfter(IncomeItem item, string periodKey) =>
        string.IsNullOrEmpty(item.ToPeriodKey) || string.CompareOrdinal(periodKey, item.ToPeriodKey) <= 0;
}
