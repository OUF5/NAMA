using System.Globalization;
using Api.Domain;

namespace Api.Tests;

// PRD section 3 (worked example) and 4.1 (fiscal calendar), plus R-STA-01 and R-STA-03.
public class FiscalCalendarTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    private static DateOnly Date(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // Section 3: payday 27, FY start January, today 17 Oct 2026.
    [Fact]
    public void Worked_example_month_runs_from_payday_to_the_day_before_the_next_payday()
    {
        Assert.Equal(
            new Period("2026-09", "Sep 2026", new(2026, 9, 27), new(2026, 10, 26)),
            FiscalCalendar.PeriodFor(2026, 9, 27));
        Assert.Equal("2026-09", FiscalCalendar.PeriodContaining(Today, 27).Key);
    }

    [Fact]
    public void Worked_example_statuses()
    {
        var oct = FiscalCalendar.PeriodFor(2026, 10, 27);

        Assert.Equal(PeriodStatus.InProgress, FiscalCalendar.Status(FiscalCalendar.PeriodFor(2026, 9, 27), Today));
        Assert.Equal(PeriodStatus.Closed, FiscalCalendar.Status(FiscalCalendar.PeriodFor(2026, 8, 27), Today));
        Assert.Equal(PeriodStatus.YetToCome, FiscalCalendar.Status(oct, Today));
        Assert.Equal(new DateOnly(2026, 10, 27), oct.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 26), oct.EndDate);
    }

    [Fact]
    public void Worked_example_first_fiscal_year()
    {
        var fy = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 27);

        Assert.Equal("2026-01", fy.Id);
        Assert.Equal("FY 2026", fy.Label);
        Assert.Equal(new DateOnly(2026, 1, 27), fy.StartDate);
        Assert.Equal(new DateOnly(2027, 1, 26), fy.EndDate);
        Assert.Equal(1, fy.FiscalStartMonth);
        Assert.Equal(27, fy.PaydayDay);
        Assert.False(fy.IsTransition);
        Assert.Equal(
            new[] { "Jan 2026", "Feb 2026", "Mar 2026", "Apr 2026", "May 2026", "Jun 2026",
                    "Jul 2026", "Aug 2026", "Sep 2026", "Oct 2026", "Nov 2026", "Dec 2026" },
            fy.Periods.Select(p => p.Label));
    }

    // R-CAL-01
    [Theory]
    [InlineData(2026, 2, 31, "2026-02-28")]
    [InlineData(2028, 2, 31, "2028-02-29")]
    [InlineData(2026, 2, 30, "2026-02-28")]
    [InlineData(2026, 2, 28, "2026-02-28")]
    [InlineData(2026, 4, 31, "2026-04-30")]
    [InlineData(2026, 1, 31, "2026-01-31")]
    [InlineData(2026, 3, 1, "2026-03-01")]
    public void Month_starts_on_payday_or_on_the_last_day_of_a_shorter_month(int year, int month, int payday, string expected)
    {
        Assert.Equal(Date(expected), FiscalCalendar.MonthStart(year, month, payday));
    }

    // R-CAL-01 and R-CAL-02 with payday 31.
    [Theory]
    [InlineData(1, "2026-01-31", "2026-02-27")]
    [InlineData(2, "2026-02-28", "2026-03-30")]
    [InlineData(3, "2026-03-31", "2026-04-29")]
    [InlineData(4, "2026-04-30", "2026-05-30")]
    public void Payday_31_months(int month, string start, string end)
    {
        var period = FiscalCalendar.PeriodFor(2026, month, 31);

        Assert.Equal(Date(start), period.StartDate);
        Assert.Equal(Date(end), period.EndDate);
    }

    [Theory]
    [InlineData("2026-02-27", "2026-01")]
    [InlineData("2026-02-28", "2026-02")]
    [InlineData("2026-03-30", "2026-02")]
    [InlineData("2026-03-31", "2026-03")]
    public void Payday_31_date_belongs_to_the_right_month(string date, string expectedKey)
    {
        Assert.Equal(expectedKey, FiscalCalendar.PeriodContaining(Date(date), 31).Key);
    }

    [Fact]
    public void Payday_1_gives_calendar_months()
    {
        var fy = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 1);

        Assert.All(fy.Periods, p =>
        {
            Assert.Equal(1, p.StartDate.Day);
            Assert.Equal(DateTime.DaysInMonth(p.StartDate.Year, p.StartDate.Month), p.EndDate.Day);
        });
        Assert.Equal(new DateOnly(2026, 2, 28), fy.Periods[1].EndDate);
        Assert.Equal(new DateOnly(2026, 1, 1), fy.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 31), fy.EndDate);
    }

    public static TheoryData<int, int> Settings()
    {
        var data = new TheoryData<int, int>();
        foreach (var startMonth in new[] { 1, 2, 7, 12 })
        {
            foreach (var payday in new[] { 1, 15, 27, 28, 29, 30, 31 })
            {
                data.Add(startMonth, payday);
            }
        }
        return data;
    }

    // R-CAL-02, R-CAL-04 and R-CAL-05 for many settings, across two FYs.
    [Theory]
    [MemberData(nameof(Settings))]
    public void Months_follow_each_other_without_gaps_or_overlaps(int startMonth, int payday)
    {
        var fy = FiscalCalendar.FirstFiscalYear(Today, startMonth, payday);
        var next = FiscalCalendar.NextFiscalYear(fy, startMonth, payday);
        var periods = fy.Periods.Concat(next.Periods).ToList();

        Assert.Equal(12, fy.Periods.Count);
        Assert.Equal(12, next.Periods.Count);
        Assert.Equal(startMonth, fy.Periods[0].StartDate.Month);
        Assert.Equal(startMonth, next.Periods[0].StartDate.Month);
        Assert.InRange(Today, fy.StartDate, fy.EndDate);
        Assert.Equal(fy.Periods[0].StartDate, fy.StartDate);
        Assert.Equal(fy.Periods[^1].EndDate, fy.EndDate);
        for (var i = 1; i < periods.Count; i++)
        {
            Assert.True(periods[i].StartDate <= periods[i].EndDate);
            Assert.Equal(periods[i - 1].EndDate.AddDays(1), periods[i].StartDate);
        }
    }

    // R-CAL-05
    [Fact]
    public void Today_before_payday_in_the_start_month_belongs_to_the_previous_fiscal_year()
    {
        var fy = FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 1, 10), fiscalStartMonth: 1, payday: 27);

        Assert.Equal("2025-01", fy.Id);
        Assert.Equal("FY 2025", fy.Label);
        Assert.Equal(new DateOnly(2025, 1, 27), fy.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 26), fy.EndDate);
    }

    [Theory]
    [InlineData("2026-09-26", "2026-08")]
    [InlineData("2026-09-27", "2026-09")]
    [InlineData("2026-10-26", "2026-09")]
    [InlineData("2026-10-27", "2026-10")]
    public void Payday_starts_the_new_month(string date, string expectedKey)
    {
        Assert.Equal(expectedKey, FiscalCalendar.PeriodContaining(Date(date), 27).Key);
    }

    // R-CAL-06
    [Theory]
    [InlineData("2026-10-17", 1, "2026-01", "FY 2026")]
    [InlineData("2026-10-17", 7, "2026-07", "FY 2026/27")]
    [InlineData("2026-03-01", 7, "2025-07", "FY 2025/26")]
    [InlineData("2099-10-17", 7, "2099-07", "FY 2099/00")]
    public void Fiscal_year_label(string today, int startMonth, string expectedId, string expectedLabel)
    {
        var fy = FiscalCalendar.FirstFiscalYear(Date(today), startMonth, payday: 27);

        Assert.Equal(expectedId, fy.Id);
        Assert.Equal(expectedLabel, fy.Label);
    }

    // R-CAL-08 Example A: FY 2026 is Jan to Dec 2026 with payday 27, and the start month changes to July.
    [Fact]
    public void Example_A_start_month_change_creates_a_transition_fiscal_year()
    {
        var fy2026 = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 27);

        var transition = FiscalCalendar.NextFiscalYear(fy2026, fiscalStartMonth: 7, payday: 27);

        Assert.True(transition.IsTransition);
        Assert.Equal("2027-01", transition.Id);
        Assert.Equal("FY 2027", transition.Label);
        Assert.Equal(7, transition.FiscalStartMonth);
        Assert.Equal(new DateOnly(2027, 1, 27), transition.StartDate);
        Assert.Equal(new DateOnly(2027, 7, 26), transition.EndDate);
        Assert.Equal(
            new[] { "Jan 2027", "Feb 2027", "Mar 2027", "Apr 2027", "May 2027", "Jun 2027" },
            transition.Periods.Select(p => p.Label));

        var fy2027 = FiscalCalendar.NextFiscalYear(transition, fiscalStartMonth: 7, payday: 27);

        Assert.False(fy2027.IsTransition);
        Assert.Equal("2027-07", fy2027.Id);
        Assert.Equal("FY 2027/28", fy2027.Label);
        Assert.Equal(12, fy2027.Periods.Count);
        Assert.Equal(new DateOnly(2027, 7, 27), fy2027.StartDate);
        Assert.Equal(new DateOnly(2028, 7, 26), fy2027.EndDate);
        Assert.Equal("Jul 2027", fy2027.Periods[0].Label);
        Assert.Equal("Jun 2028", fy2027.Periods[^1].Label);
    }

    // R-CAL-08: the transition FY ends just before the new start month.
    [Theory]
    [InlineData(1, 7, 6)]
    [InlineData(7, 1, 6)]
    [InlineData(1, 2, 1)]
    [InlineData(1, 12, 11)]
    [InlineData(7, 8, 1)]
    [InlineData(7, 6, 11)]
    [InlineData(7, 7, 12)]
    public void Transition_fiscal_year_length(int oldStartMonth, int newStartMonth, int expectedMonths)
    {
        var fy = FiscalCalendar.FirstFiscalYear(Today, oldStartMonth, payday: 27);

        var next = FiscalCalendar.NextFiscalYear(fy, newStartMonth, payday: 27);

        Assert.Equal(expectedMonths, next.Periods.Count);
        Assert.Equal(expectedMonths != 12, next.IsTransition);
        Assert.Equal(fy.EndDate.AddDays(1), next.StartDate);
        var after = FiscalCalendar.NextFiscalYear(next, newStartMonth, payday: 27);
        Assert.Equal(newStartMonth, after.Periods[0].StartDate.Month);
        Assert.Equal(12, after.Periods.Count);
    }

    // R-CAL-08 Example B: payday changes from 27 to 1.
    [Fact]
    public void Example_B_payday_change_gives_a_shorter_first_month()
    {
        var fy2026 = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 27);

        var next = FiscalCalendar.NextFiscalYear(fy2026, fiscalStartMonth: 1, payday: 1);

        Assert.False(next.IsTransition);
        Assert.Equal("2027-01", next.Id);
        Assert.Equal("FY 2027", next.Label);
        Assert.Equal(1, next.PaydayDay);
        Assert.Equal(12, next.Periods.Count);
        Assert.Equal(new Period("2027-01", "Jan 2027", new(2027, 1, 27), new(2027, 1, 31)), next.Periods[0]);
        Assert.Equal(new Period("2027-02", "Feb 2027", new(2027, 2, 1), new(2027, 2, 28)), next.Periods[1]);
        Assert.All(next.Periods.Skip(1), p => Assert.Equal(1, p.StartDate.Day));
        Assert.Equal(new DateOnly(2027, 12, 31), next.EndDate);
    }

    [Fact]
    public void Payday_change_from_1_to_27_gives_a_longer_first_month()
    {
        var fy = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 1);

        var next = FiscalCalendar.NextFiscalYear(fy, fiscalStartMonth: 1, payday: 27);

        Assert.Equal(new Period("2027-01", "Jan 2027", new(2027, 1, 1), new(2027, 2, 26)), next.Periods[0]);
        Assert.Equal(new Period("2027-02", "Feb 2027", new(2027, 2, 27), new(2027, 3, 26)), next.Periods[1]);
        Assert.Equal(new DateOnly(2028, 1, 26), next.EndDate);
    }

    [Fact]
    public void Unchanged_settings_continue_with_the_next_12_months()
    {
        var fy2026 = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 27);

        var next = FiscalCalendar.NextFiscalYear(fy2026, fiscalStartMonth: 1, payday: 27);
        var expected = FiscalCalendar.FirstFiscalYear(new DateOnly(2027, 10, 17), fiscalStartMonth: 1, payday: 27);

        Assert.False(next.IsTransition);
        Assert.Equal(expected.Id, next.Id);
        Assert.Equal(expected.Label, next.Label);
        Assert.Equal(expected.StartDate, next.StartDate);
        Assert.Equal(expected.EndDate, next.EndDate);
        Assert.Equal(expected.Periods, next.Periods);
    }

    [Fact]
    public void Start_month_and_payday_change_together()
    {
        var fy2026 = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 27);

        var transition = FiscalCalendar.NextFiscalYear(fy2026, fiscalStartMonth: 7, payday: 1);

        Assert.True(transition.IsTransition);
        Assert.Equal(6, transition.Periods.Count);
        Assert.Equal(new Period("2027-01", "Jan 2027", new(2027, 1, 27), new(2027, 1, 31)), transition.Periods[0]);
        Assert.Equal(new Period("2027-06", "Jun 2027", new(2027, 6, 1), new(2027, 6, 30)), transition.Periods[^1]);
        Assert.Equal(new DateOnly(2027, 6, 30), transition.EndDate);

        var fy2027 = FiscalCalendar.NextFiscalYear(transition, fiscalStartMonth: 7, payday: 1);

        Assert.Equal("FY 2027/28", fy2027.Label);
        Assert.Equal(new DateOnly(2027, 7, 1), fy2027.StartDate);
        Assert.Equal(new DateOnly(2028, 6, 30), fy2027.EndDate);
    }

    // R-CAL-07: starting the next FY never changes the current one.
    [Fact]
    public void Next_fiscal_year_keeps_the_current_fiscal_year_dates()
    {
        var fy = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 27);
        var periodsBefore = fy.Periods.ToList();

        FiscalCalendar.NextFiscalYear(fy, fiscalStartMonth: 7, payday: 1);

        Assert.Equal(periodsBefore, fy.Periods);
        Assert.Equal(new DateOnly(2027, 1, 26), fy.EndDate);
        Assert.Equal(1, fy.FiscalStartMonth);
        Assert.Equal(27, fy.PaydayDay);
    }

    // R-STA-01
    [Theory]
    [InlineData("2026-09-26", PeriodStatus.YetToCome)]
    [InlineData("2026-09-27", PeriodStatus.InProgress)]
    [InlineData("2026-10-26", PeriodStatus.InProgress)]
    [InlineData("2026-10-27", PeriodStatus.Closed)]
    public void Status_is_derived_from_dates(string today, PeriodStatus expected)
    {
        Assert.Equal(expected, FiscalCalendar.Status(FiscalCalendar.PeriodFor(2026, 9, 27), Date(today)));
    }

    // DSH-02: a month counts towards "FY to date" from its first day on (In progress or Closed).
    [Theory]
    [InlineData("2026-09-26", false)]
    [InlineData("2026-09-27", true)]
    [InlineData("2026-10-26", true)]
    [InlineData("2026-10-27", true)]
    public void Has_started_from_the_first_day_of_the_month(string today, bool expected)
    {
        Assert.Equal(expected, FiscalCalendar.HasStarted(FiscalCalendar.PeriodFor(2026, 9, 27), Date(today)));
    }

    // R-CAL-10
    [Theory]
    [InlineData("Asia/Riyadh", "2026-10-17")]
    [InlineData("America/New_York", "2026-10-16")]
    [InlineData("UTC", "2026-10-16")]
    public void Today_is_the_date_in_the_profile_time_zone(string timeZoneId, string expected)
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 16, 22, 30, 0, TimeSpan.Zero));

        Assert.Equal(Date(expected), FiscalCalendar.Today(clock, timeZoneId));
    }

    // R-CAL-09 and R-STA-03: an entry date maps to its FY and month, or to nothing.
    [Theory]
    [InlineData("2026-01-27", "2026-01", "2026-01")]
    [InlineData("2026-10-17", "2026-01", "2026-09")]
    [InlineData("2027-01-26", "2026-01", "2026-12")]
    [InlineData("2027-01-27", "2027-01", "2027-01")]
    [InlineData("2028-01-26", "2027-01", "2027-12")]
    public void Date_inside_a_fiscal_year_finds_its_month(string date, string expectedFyId, string expectedKey)
    {
        var fy2026 = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 27);
        var fy2027 = FiscalCalendar.NextFiscalYear(fy2026, fiscalStartMonth: 1, payday: 27);

        var found = FiscalCalendar.FindPeriod([fy2026, fy2027], Date(date));

        Assert.NotNull(found);
        Assert.Equal(expectedFyId, found.Value.FiscalYear.Id);
        Assert.Equal(expectedKey, found.Value.Period.Key);
    }

    [Theory]
    [InlineData("2026-01-26")]
    [InlineData("2028-01-27")]
    public void Date_outside_every_fiscal_year_finds_nothing(string date)
    {
        var fy2026 = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 27);
        var fy2027 = FiscalCalendar.NextFiscalYear(fy2026, fiscalStartMonth: 1, payday: 27);

        Assert.Null(FiscalCalendar.FindPeriod([fy2026, fy2027], Date(date)));
    }

    [Theory]
    [InlineData("2026-03", "2026-02")]
    [InlineData("2026-01", "2025-12")]
    public void Previous_key(string key, string expected)
    {
        Assert.Equal(expected, FiscalCalendar.PreviousKey(key));
    }

    [Theory]
    [InlineData("2027-03", "2026-03")]
    [InlineData("2028-01", "2027-01")]
    public void Year_earlier_key(string key, string expected)
    {
        Assert.Equal(expected, FiscalCalendar.YearEarlierKey(key));
    }

    // FY-01, FY-02: Dec 2026 (27 Dec 2026 to 26 Jan 2027) is the last month of FY 2026.
    [Theory]
    [InlineData("2026-10-17", false)]
    [InlineData("2026-12-26", false)]
    [InlineData("2026-12-27", true)]
    [InlineData("2027-01-26", true)]
    [InlineData("2027-03-01", true)]
    public void Next_fiscal_year_can_start_in_the_last_month_and_after(string today, bool expected)
    {
        var fy2026 = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 27);

        Assert.Equal(expected, FiscalCalendar.CanStartNext(fy2026, Date(today)));
    }

    [Fact]
    public void Latest_is_the_fiscal_year_that_starts_last()
    {
        var fy2026 = FiscalCalendar.FirstFiscalYear(Today, fiscalStartMonth: 1, payday: 27);
        var transition = FiscalCalendar.NextFiscalYear(fy2026, fiscalStartMonth: 7, payday: 27);

        Assert.Equal(transition, FiscalCalendar.Latest([transition, fy2026]));
        Assert.Null(FiscalCalendar.Latest([]));
    }

    // PRD 4.7: payday is 1 to 31, FY start month is 1 to 12.
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 32)]
    [InlineData(0, 27)]
    [InlineData(13, 27)]
    public void Out_of_range_settings_are_rejected(int startMonth, int payday)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FiscalCalendar.FirstFiscalYear(Today, startMonth, payday));
    }
}
