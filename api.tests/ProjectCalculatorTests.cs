using System.Globalization;
using Api.Domain;

namespace Api.Tests;

// PRD 4.11 saving projects (owner note 30): the worked examples of R-PRJ-06, R-PRJ-07 and R-PRJ-08,
// and every status of R-PRJ-10, in minor units. The money comes from a made-up timeline here; the
// API tests read it from real balances.
public class ProjectCalculatorTests
{
    private static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // R-PRJ-07's example: payday 25, FY from 25 Jan 2026, months on after it with the same settings.
    private static readonly IReadOnlyList<Period> Months = FiscalCalendar.MonthsThrough(
        [FiscalCalendar.FirstFiscalYear(Date("2026-10-31"), 1, 25)], Date("2027-12-31"), 1, 25, MonthNaming.EndMonth);

    private static Project Project(
        string deadline = "2027-08-30",
        ProjectStatus status = ProjectStatus.Active,
        string? doneOn = null,
        long monthly = 700_000,
        IReadOnlyList<MonthlyAmount>? amounts = null,
        IReadOnlyList<Milestone>? milestones = null,
        TargetLevel active = TargetLevel.Likely,
        string start = "2026-10") => new(
        "de",
        "Road to DE",
        Date(deadline),
        status,
        doneOn is null ? null : Date(doneOn),
        new Dictionary<TargetLevel, long>
        {
            [TargetLevel.Best] = 12_600_000,
            [TargetLevel.Likely] = 14_400_000,
            [TargetLevel.Worst] = 18_400_000,
        },
        active,
        "alinma",
        amounts ?? Months.Where(period => string.CompareOrdinal(period.Key, start) >= 0)
            .Select(period => new MonthlyAmount(period.Key, monthly)).ToList(),
        milestones ?? [],
        start);

    // Alinma Saving held 64,175 until 1,200 went in on 26 Oct 2026.
    private static long? Money(DateOnly day) => day <= Date("2026-10-26") ? 6_417_500 : 6_537_500;

    // R-PRJ-07 worked example: progress 65,375, 5,800 still to put in this month, 10 months of 7,000
    // to come: 141,175, short 2,825, 257 more a month over 11 months.
    [Fact]
    public void Forecast_matches_the_worked_example()
    {
        var figures = ProjectCalculator.Figures(Project(), Date("2026-10-31"), Months, Money);

        Assert.Equal(6_537_500, figures.Money);
        Assert.Equal(6_537_500, figures.Progress);
        Assert.Equal(7_862_500, figures.StillToSave);
        Assert.Equal(45, figures.Percent);
        Assert.Equal("2026-10", figures.CurrentMonth!.Key);
        Assert.Equal(580_000, figures.StillToPutIn);
        Assert.Equal(10, figures.MonthsToCome.Count);
        Assert.Equal(Date("2026-11-25"), figures.MonthsToCome[0].Period.StartDate);
        Assert.Equal(Date("2027-08-25"), figures.MonthsToCome[^1].Period.StartDate);
        Assert.Equal(14_117_500, figures.Projected);
        Assert.Equal(282_500, figures.Short);
        Assert.Equal(11, figures.MonthsLeft);
        Assert.Equal(25_700, figures.ExtraAMonth);
        Assert.Equal(ProjectState.OffTrack, figures.State);
        Assert.True(figures.Covers(12_600_000));
        Assert.False(figures.Covers(14_400_000));
        Assert.False(figures.Covers(18_400_000));
    }

    // R-PRJ-08 example: Deposit 50,116 by 1 May 2027: 65,375 + 5,800 + 6 × 7,000 = 113,175.
    [Fact]
    public void A_milestone_to_come_is_compared_with_the_projected_money()
    {
        var project = Project(milestones: [new Milestone("m1", "Deposit", Date("2027-05-01"), 5_011_600)]);

        var check = Assert.Single(ProjectCalculator.Figures(project, Date("2026-10-31"), Months, Money).Milestones);

        Assert.False(check.Passed);
        Assert.Equal(11_317_500, check.Money);
        Assert.Equal(6_305_900, check.Difference);
    }

    // FR7: on track at the end means nothing if a mid-point is missed.
    [Fact]
    public void A_short_milestone_to_come_is_off_track_even_when_the_end_is_covered()
    {
        var project = Project(
            monthly: 1_000_000,
            milestones: [new Milestone("m1", "Deposit", Date("2026-12-01"), 9_000_000)]);

        var figures = ProjectCalculator.Figures(project, Date("2026-10-31"), Months, Money);

        Assert.True(figures.Projected >= figures.Target);
        // 65,375 + 8,800 + 10,000 (25 Nov) = 84,175: 5,825 short of 90,000.
        Assert.Equal(-582_500, figures.Milestones[0].Difference);
        Assert.Equal(ProjectState.OffTrack, figures.State);
    }

    // R-PRJ-08: a passed milestone uses the money at the start of its day, so paying it that day still meets it.
    [Fact]
    public void A_passed_milestone_uses_the_money_at_the_start_of_its_day()
    {
        var project = Project(milestones:
        [
            new Milestone("met", "Met", Date("2026-10-27"), 6_537_500),
            new Milestone("missed", "Missed", Date("2026-10-26"), 6_537_500),
        ]);

        var checks = ProjectCalculator.Figures(project, Date("2026-10-31"), Months, Money).Milestones;

        Assert.Equal(["Missed", "Met"], checks.Select(check => check.Milestone.Label));
        Assert.All(checks, check => Assert.True(check.Passed));
        Assert.Equal(-120_000, checks[0].Difference);
        Assert.Equal(0, checks[1].Difference);
    }

    // R-PRJ-06 example: monthly 2,000; Oct 1,200 in (800 behind), Nov 2,500 (300 behind).
    [Fact]
    public void Planned_vs_actual_carries_what_is_behind()
    {
        long? money(DateOnly day) => day <= Date("2026-10-25") ? 1_000_000
            : day <= Date("2026-11-25") ? 1_120_000
            : 1_370_000;
        var project = Project(monthly: 200_000, start: "2026-10");

        var figures = ProjectCalculator.Figures(project, Date("2026-12-26"), Months, money);

        Assert.Equal(["2026-10", "2026-11", "2026-12"], figures.Months.Select(month => month.Period.Key));
        Assert.Equal(new long?[] { 120_000, 250_000, 0 }, figures.Months.Select(month => month.Actual));
        Assert.Equal(new long[] { 80_000, 30_000, 230_000 }, figures.Months.Select(month => month.Behind));
        Assert.Equal(30_000, figures.BehindBefore);
        Assert.Equal(200_000, figures.StillToPutIn);
    }

    // R-PRJ-06: a month with unknown money shows no actual and carries nothing; this month's still to
    // put in is then its whole amount.
    [Fact]
    public void Unknown_money_at_the_month_start_carries_nothing()
    {
        long? money(DateOnly day) => day < Date("2026-10-28") ? null : 5_000_000;

        var figures = ProjectCalculator.Figures(Project(), Date("2026-10-31"), Months, money);

        var month = Assert.Single(figures.Months);
        Assert.Null(month.Actual);
        Assert.Equal(0, month.Behind);
        Assert.Equal(700_000, figures.StillToPutIn);
        Assert.Equal(5_000_000, figures.Progress);
    }

    // R-PRJ-06: an account opened inside the month counts from its opening day (owner's local test,
    // 31 Oct 2026: "Put in" was unknown for an account opened this month).
    [Fact]
    public void A_month_counts_from_the_opening_day_of_an_account_opened_in_it()
    {
        long? money(DateOnly day) => day < Date("2026-10-28") ? null : day <= Date("2026-10-30") ? 5_000_000 : 5_100_000;

        var figures = ProjectCalculator.Figures(Project(), Date("2026-10-31"), Months, money, openedOn: Date("2026-10-28"));

        var month = Assert.Single(figures.Months);
        Assert.Equal(100_000, month.Actual);
        Assert.Equal(600_000, figures.StillToPutIn);
        Assert.Equal(Date("2026-10-25"), ProjectCalculator.CountedFrom(month.Period, Date("2026-09-01")));
        Assert.Equal(Date("2026-10-25"), ProjectCalculator.CountedFrom(month.Period, Date("2026-12-01")));
    }

    [Fact]
    public void Unknown_money_today_is_unknown()
    {
        var figures = ProjectCalculator.Figures(Project(), Date("2026-10-31"), Months, _ => null);

        Assert.Null(figures.Progress);
        Assert.Null(figures.Projected);
        Assert.Null(figures.Percent);
        Assert.Equal(ProjectState.Unknown, figures.State);
    }

    // R-PRJ-10
    [Fact]
    public void Progress_at_the_target_is_reached()
    {
        var figures = ProjectCalculator.Figures(Project(active: TargetLevel.Best), Date("2026-10-31"), Months, _ => 12_600_000);

        Assert.Equal(ProjectState.Reached, figures.State);
        Assert.Equal(0, figures.StillToSave);
        Assert.Equal(100, figures.Percent);
    }

    [Fact]
    public void A_covered_projection_is_on_track()
    {
        var figures = ProjectCalculator.Figures(Project(monthly: 800_000), Date("2026-10-31"), Months, Money);

        // 65,375 + 6,800 + 80,000 = 152,175.
        Assert.Equal(15_217_500, figures.Projected);
        Assert.Equal(0, figures.Short);
        Assert.Equal(0, figures.ExtraAMonth);
        Assert.Equal(ProjectState.OnTrack, figures.State);
    }

    // R-PRJ-07: after the deadline nothing is projected, and a target not reached is off track.
    [Fact]
    public void After_the_deadline_nothing_is_projected()
    {
        var figures = ProjectCalculator.Figures(Project(deadline: "2026-10-30"), Date("2026-10-31"), Months, Money);

        Assert.True(figures.DeadlinePassed);
        Assert.Null(figures.Projected);
        Assert.Empty(figures.MonthsToCome);
        Assert.Equal(0, figures.MonthsLeft);
        Assert.Equal(ProjectState.OffTrack, figures.State);
        Assert.True(figures.Covers(6_000_000));
    }

    // R-PRJ-07: a deadline in the month in progress counts this month only.
    [Fact]
    public void A_deadline_this_month_needs_the_short_amount_now()
    {
        var figures = ProjectCalculator.Figures(Project(deadline: "2026-11-20"), Date("2026-10-31"), Months, Money);

        Assert.Empty(figures.MonthsToCome);
        Assert.Equal(1, figures.MonthsLeft);
        // 144,000 − (65,375 + 5,800) = 72,825.
        Assert.Equal(7_282_500, figures.ExtraAMonth);
    }

    // R-PRJ-01: a Paused project keeps its amounts but puts nothing more in.
    [Fact]
    public void A_paused_project_adds_nothing()
    {
        var project = Project(status: ProjectStatus.Paused, start: "2026-09");

        var figures = ProjectCalculator.Figures(project, Date("2026-10-31"), Months, Money);

        Assert.Equal(ProjectState.Paused, figures.State);
        Assert.Equal(0, figures.StillToPutIn);
        Assert.Empty(figures.MonthsToCome);
        Assert.Equal(figures.Progress, figures.Projected);
    }

    // R-PRJ-01: a Done project's figures stay as they were on its day.
    [Fact]
    public void A_done_project_stays_as_it_was_on_its_day()
    {
        var project = Project(status: ProjectStatus.Done, doneOn: "2026-10-25", milestones:
            [new Milestone("m1", "Later", Date("2027-01-01"), 100)]);

        var figures = ProjectCalculator.Figures(project, Date("2027-02-01"), Months, Money);

        // The end of 25 Oct, before the 1,200 of 26 Oct.
        Assert.Equal(Date("2026-10-25"), figures.AsOf);
        Assert.Equal(6_417_500, figures.Money);
        Assert.Null(figures.Projected);
        Assert.Null(figures.Milestones[0].Money);
        Assert.Equal(ProjectState.Done, figures.State);
    }

    // R-PRJ-12 example: 144,000 − 64,175 (the money on 25 Oct) over the 11 months from Oct 2026 to the
    // one that holds 30 Aug 2027 = 7,256.82, rounded up to 7,257.
    [Fact]
    public void The_recommended_amount_reaches_the_target()
    {
        var months = ProjectCalculator.PlanMonths(Months, "2026-10", Date("2027-08-30"));
        Assert.Equal(11, months.Count);
        Assert.Equal(Date("2027-08-25"), months[^1].StartDate);

        var recommended = ProjectCalculator.Recommended(14_400_000, Money(Date("2026-10-25")), months.Count);
        Assert.Equal(725_700, recommended);

        // Saved every month, it brings the projection to the target: 65,375 + 6,057 + 10 × 7,257 = 144,002.
        var figures = ProjectCalculator.Figures(Project(monthly: recommended!.Value), Date("2026-10-31"), Months, Money);
        Assert.Equal(14_400_200, figures.Projected);
        Assert.Equal(ProjectState.OnTrack, figures.State);
    }

    [Fact]
    public void The_recommended_amount_is_0_once_there_and_unknown_without_money_or_months()
    {
        Assert.Equal(0, ProjectCalculator.Recommended(100_000, 150_000, 3));
        Assert.Null(ProjectCalculator.Recommended(100_000, null, 3));
        Assert.Null(ProjectCalculator.Recommended(100_000, 0, 0));
    }

    // R-PRJ-05: each month has its own amount; a month with none is 0.
    [Fact]
    public void Each_month_has_its_own_amount()
    {
        IReadOnlyList<MonthlyAmount> amounts = [new("2026-10", 200_000), new("2026-12", 300_000)];

        Assert.Equal(200_000, ProjectCalculator.MonthlyFor(amounts, "2026-10"));
        Assert.Equal(0, ProjectCalculator.MonthlyFor(amounts, "2026-11"));
        Assert.Equal(300_000, ProjectCalculator.MonthlyFor(amounts, "2026-12"));

        // 65,375 + (2,000 − 1,200) + 0 (Nov) + 3,000 (Dec) by 30 Dec 2026.
        var figures = ProjectCalculator.Figures(Project(deadline: "2026-12-30", amounts: amounts), Date("2026-10-31"), Months, Money);
        Assert.Equal(6_917_500, figures.Projected);
    }
}
