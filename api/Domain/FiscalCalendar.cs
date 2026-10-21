using System.Globalization;

namespace Api.Domain;

// R-STA-01: derived from dates, never stored.
public enum PeriodStatus { Closed, InProgress, YetToCome }

// One payday cycle (PRD 3 "Month"). Key is YYYY-MM of the start date.
public sealed record Period(string Key, string Label, DateOnly StartDate, DateOnly EndDate);

// PRD 7.4 fiscal year document. Id is the key of its first month. FiscalStartMonth and
// PaydayDay are the settings it was created with.
public sealed record FiscalYear(
    string Id,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    int FiscalStartMonth,
    int PaydayDay,
    bool IsTransition,
    IReadOnlyList<Period> Periods);

// PRD 4.1: every month and FY rule. Pure functions; "today" is always passed in.
public static class FiscalCalendar
{
    private const int MonthsPerYear = 12;

    // R-CAL-01: payday, or the last day of a calendar month that is shorter than the payday.
    public static DateOnly MonthStart(int year, int month, int payday)
    {
        CheckPayday(payday);
        return new DateOnly(year, month, Math.Min(payday, DateTime.DaysInMonth(year, month)));
    }

    // R-CAL-01 to R-CAL-03: labeled by the month it starts in, ends the day before the next month starts.
    public static Period PeriodFor(int year, int month, int payday)
    {
        var next = new DateOnly(year, month, 1).AddMonths(1);
        return NewPeriod(MonthStart(year, month, payday), MonthStart(next.Year, next.Month, payday).AddDays(-1));
    }

    public static Period PeriodContaining(DateOnly date, int payday)
    {
        var month = new DateOnly(date.Year, date.Month, 1);
        if (date < MonthStart(date.Year, date.Month, payday))
        {
            month = month.AddMonths(-1);
        }
        return PeriodFor(month.Year, month.Month, payday);
    }

    // R-CAL-04, R-CAL-05: the 12 months that contain today, starting with the FY start month.
    public static FiscalYear FirstFiscalYear(DateOnly today, int fiscalStartMonth, int payday)
    {
        CheckStartMonth(fiscalStartMonth);
        var current = FirstOfMonth(PeriodContaining(today, payday).StartDate);
        var first = current.AddMonths(-((current.Month - fiscalStartMonth + MonthsPerYear) % MonthsPerYear));
        var periods = Enumerable.Range(0, MonthsPerYear)
            .Select(i => first.AddMonths(i))
            .Select(m => PeriodFor(m.Year, m.Month, payday))
            .ToList();
        return NewFiscalYear(periods, fiscalStartMonth, payday, isTransition: false);
    }

    // R-CAL-07, R-CAL-08: starts the day after the current FY ends and uses the current settings.
    // When the start month changed, it is a transition FY that ends just before the new start month.
    public static FiscalYear NextFiscalYear(FiscalYear current, int fiscalStartMonth, int payday)
    {
        CheckStartMonth(fiscalStartMonth);
        CheckPayday(payday);
        var first = FirstOfMonth(current.Periods[^1].StartDate).AddMonths(1);
        var isTransition = first.Month != fiscalStartMonth;
        var count = isTransition ? (fiscalStartMonth - first.Month + MonthsPerYear) % MonthsPerYear : MonthsPerYear;
        var periods = Enumerable.Range(0, count)
            .Select(i => first.AddMonths(i))
            .Select(m => PeriodFor(m.Year, m.Month, payday))
            .ToList();
        // The first month can be shorter or longer when the payday changed.
        periods[0] = NewPeriod(current.EndDate.AddDays(1), periods[0].EndDate);
        return NewFiscalYear(periods, fiscalStartMonth, payday, isTransition);
    }

    // R-CAL-06: "FY 2026" when all month labels fall in one calendar year, otherwise "FY 2026/27".
    public static string FiscalYearLabel(IReadOnlyList<Period> periods)
    {
        var firstYear = periods[0].StartDate.Year;
        var lastYear = periods[^1].StartDate.Year;
        return firstYear == lastYear
            ? FormattableString.Invariant($"FY {firstYear}")
            : FormattableString.Invariant($"FY {firstYear}/{lastYear % 100:00}");
    }

    // R-STA-01
    public static PeriodStatus Status(Period period, DateOnly today) =>
        today > period.EndDate ? PeriodStatus.Closed
        : today < period.StartDate ? PeriodStatus.YetToCome
        : PeriodStatus.InProgress;

    // DSH-02 "FY to date" (owner's choice, M9): a month counts from its first day on, so In progress
    // and Closed months count and Yet to come ones do not.
    public static bool HasStarted(Period period, DateOnly today) => period.StartDate <= today;

    // R-CAL-10: today's date in the profile time zone (IANA id).
    public static DateOnly Today(TimeProvider timeProvider, string timeZoneId) => DateIn(timeProvider.GetUtcNow(), timeZoneId);

    // The date of a moment in a time zone, for example when an invite link expires (AUTH-07).
    public static DateOnly DateIn(DateTimeOffset instant, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    // R-CAL-09, R-STA-03: the stored FY and month of a date, or null when no FY contains it.
    public static (FiscalYear FiscalYear, Period Period)? FindPeriod(IEnumerable<FiscalYear> fiscalYears, DateOnly date)
    {
        foreach (var fiscalYear in fiscalYears)
        {
            foreach (var period in fiscalYear.Periods)
            {
                if (period.StartDate <= date && date <= period.EndDate)
                {
                    return (fiscalYear, period);
                }
            }
        }
        return null;
    }

    // The stored FY with the latest start date, or null when there is none.
    public static FiscalYear? Latest(IEnumerable<FiscalYear> fiscalYears) =>
        fiscalYears.MaxBy(fiscalYear => fiscalYear.StartDate);

    // FY-01, FY-02: the next FY can be started during the last month of the latest FY and after it ends.
    public static bool CanStartNext(FiscalYear latest, DateOnly today) => today >= latest.Periods[^1].StartDate;

    // The month key before a YYYY-MM key.
    public static string PreviousKey(string key) => AddMonthsToKey(key, -1);

    // Owner note 9 (Tracker's month): every day of a month, payday first.
    public static IReadOnlyList<DateOnly> Days(Period period) =>
        Enumerable.Range(0, period.EndDate.DayNumber - period.StartDate.DayNumber + 1)
            .Select(period.StartDate.AddDays)
            .ToList();

    // The stored month before a month, in its FY or the FY before; null for the first stored month.
    public static (FiscalYear FiscalYear, Period Period)? PreviousPeriod(IEnumerable<FiscalYear> fiscalYears, string periodKey)
    {
        var key = PreviousKey(periodKey);
        foreach (var fiscalYear in fiscalYears)
        {
            if (fiscalYear.Periods.FirstOrDefault(period => period.Key == key) is { } period)
            {
                return (fiscalYear, period);
            }
        }
        return null;
    }

    // FY-02 plan copy (owner's choice, M8): the same month a year earlier.
    public static string YearEarlierKey(string key) => AddMonthsToKey(key, -MonthsPerYear);

    private static string AddMonthsToKey(string key, int months) =>
        Key(DateOnly.ParseExact(key, "yyyy-MM", CultureInfo.InvariantCulture).AddMonths(months));

    private static Period NewPeriod(DateOnly start, DateOnly end) =>
        new(Key(start), start.ToString("MMM yyyy", CultureInfo.InvariantCulture), start, end);

    private static FiscalYear NewFiscalYear(List<Period> periods, int fiscalStartMonth, int payday, bool isTransition) =>
        new(periods[0].Key, FiscalYearLabel(periods), periods[0].StartDate, periods[^1].EndDate,
            fiscalStartMonth, payday, isTransition, periods.AsReadOnly());

    private static string Key(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    // PRD 4.7
    private static void CheckPayday(int payday)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(payday, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(payday, 31);
    }

    private static void CheckStartMonth(int fiscalStartMonth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fiscalStartMonth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fiscalStartMonth, MonthsPerYear);
    }
}
