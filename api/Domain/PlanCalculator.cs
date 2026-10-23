namespace Api.Domain;

// PRD 7.4 plan document: the planned amounts of one sub-category in one FY, by month key.
// Only amounts above 0 are stored.
public sealed record PlanAmounts(string SubCategoryId, string CategoryId, IReadOnlyDictionary<string, long> Amounts);

// R-PLN-03: one month of Plan's totals. CarriedIn is the previous month's Unassigned.
public sealed record UnassignedMonth(string PeriodKey, long CarriedIn, long Income, long Planned)
{
    // The month on its own, not carried: its income minus its planned amounts.
    public long ThisMonth => Income - Planned;

    public long Unassigned => CarriedIn + ThisMonth;
}

// R-PLN-05: one month of a plan line (a sub-category, a category or the whole month).
public sealed record CarriedMonth(long CarriedIn, long Planned, long Spent)
{
    public long Available => CarriedIn + Planned;

    public long Remaining => Available - Spent;
}

// R-PLN-07, R-PLN-08 (owner note 8): how the FY that contains today ends if every month from now on
// is spent as planned. Leftovers carry (R-PLN-05), so they are spent: only this month's remaining
// above 0 is still to spend. FutureMonths are the months yet to come, each with its income minus its
// planned (ThisMonth). BalanceToday is null until the balance is complete (BNK-05).
public sealed record Projection(
    string CurrentPeriodKey,
    long StillToSpend,
    IReadOnlyList<UnassignedMonth> FutureMonths,
    long SavedToDate,
    long PlannedSaved,
    long? BalanceToday)
{
    public long FutureUnallocated => FutureMonths.Sum(month => month.ThisMonth);

    public long? ProjectedBalance => BalanceToday - StillToSpend + FutureUnallocated;

    public long ProjectedSaving => SavedToDate - StillToSpend + FutureUnallocated;

    // Owner note 9 (Tracker's Year view): the projected balance at the end of this month and of each
    // month yet to come, by month key. The last one is ProjectedBalance.
    public IReadOnlyList<(string PeriodKey, long? Balance)> MonthEnds()
    {
        var end = BalanceToday - StillToSpend;
        var ends = new List<(string, long?)> { (CurrentPeriodKey, end) };
        foreach (var month in FutureMonths)
        {
            end += month.ThisMonth;
            ends.Add((month.PeriodKey, end));
        }
        return ends;
    }
}

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

    // R-PLN-06 (20 Oct 2026): NAMA counts from the month you started it. The first month that carries
    // is the one that contains the day setup was finished, or the FY's first month when that is later
    // (each FY closes on its own). Months before it were never tracked, so nothing is carried out of
    // them: they stand alone. Without a start day (tests, older data) the FY's first month carries.
    public static int CarryStart(IReadOnlyList<Period> periods, DateOnly? startedOn)
    {
        if (startedOn is not { } day)
        {
            return 0;
        }
        var index = periods.Count(period => period.EndDate < day);
        return Math.Min(index, periods.Count);
    }

    // R-PLN-03 (changed 20 Oct 2026): Unassigned carries from month to month. A month's Unassigned is
    // the previous month's Unassigned plus its income minus its planned amounts, from 0 in the first
    // month that carries (R-PLN-06); earlier months stand alone. Negative means more is planned than
    // received so far.
    public static IReadOnlyList<UnassignedMonth> RunningUnassigned(
        IEnumerable<Period> periods, IReadOnlyDictionary<string, long> income, IEnumerable<PlanAmounts> plans, int carryStart = 0)
    {
        var planList = plans.ToList();
        var months = new List<UnassignedMonth>();
        var carried = 0L;
        foreach (var (period, i) in periods.Select((period, i) => (period, i)))
        {
            if (i <= carryStart)
            {
                carried = 0;
            }
            var month = new UnassignedMonth(period.Key, carried, income.GetValueOrDefault(period.Key), MonthPlanned(planList, period.Key));
            months.Add(month);
            carried = month.Unassigned;
        }
        return months;
    }

    // R-PLN-04: what the plan leaves unassigned at the end of the FY: the last month's Unassigned, that
    // is income minus planned from the first month that carries to the FY's end.
    public static long PlannedSaved(IReadOnlyList<UnassignedMonth> months) => months.Count == 0 ? 0 : months[^1].Unassigned;

    // R-PLN-05: what is left of a plan carries into the next month, positive or negative. Each month's
    // available amount is what carried in plus its plan. Nothing carries into the first month that
    // carries (R-PLN-06), and months before it stand alone.
    public static IReadOnlyList<CarriedMonth> Carry(IEnumerable<(long Planned, long Spent)> months, int carryStart = 0)
    {
        var result = new List<CarriedMonth>();
        var carried = 0L;
        foreach (var ((planned, spent), i) in months.Select((month, i) => (month, i)))
        {
            if (i <= carryStart)
            {
                carried = 0;
            }
            var month = new CarriedMonth(carried, planned, spent);
            result.Add(month);
            carried = month.Remaining;
        }
        return result;
    }

    // R-PLN-07, R-PLN-08. Months is the whole FY's Unassigned (R-PLN-03, planned income); toToday is
    // the whole budget carried (R-PLN-05) from the FY's first month to the month in progress, its last
    // item. Saved to date is actual income (month key → amount, R-INC-01) minus spent, from the first
    // month that carries (R-PLN-06), like DSH-02.
    public static Projection Project(
        IReadOnlyList<UnassignedMonth> months, IReadOnlyList<CarriedMonth> toToday, int carryStart, long? balanceToday,
        IReadOnlyDictionary<string, long> actualIncome)
    {
        var current = toToday.Count - 1;
        var saved = Enumerable.Range(0, toToday.Count)
            .Where(i => i >= carryStart)
            .Sum(i => Saved(actualIncome.GetValueOrDefault(months[i].PeriodKey), toToday[i].Spent));
        return new Projection(
            months[current].PeriodKey,
            Math.Max(0, toToday[current].Remaining),
            months.Skip(current + 1).ToList(),
            saved,
            PlannedSaved(months),
            balanceToday);
    }

    // ENT-01: available minus spent; negative means over plan.
    public static long Remaining(long available, long spent) => available - spent;

    // DSH-02: income minus spent; negative means more was spent than earned.
    public static long Saved(long income, long spent) => income - spent;

    // ENT-01: share of the available amount spent, rounded down (79.99% is 79). Null when nothing is available.
    public static int? Percent(long available, long spent) =>
        available <= 0 ? null : (int)Math.Min(spent * 100 / available, int.MaxValue);

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
