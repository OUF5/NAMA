namespace Api.Domain;

// R-PRJ-01 (owner note 30): a new project is Active.
public enum ProjectStatus { Active, Paused, Done }

// R-PRJ-02: the three targets.
public enum TargetLevel { Best, Likely, Worst }

// R-PRJ-10: what a project's status chip says.
public enum ProjectState { OnTrack, OffTrack, Reached, Paused, Done, Unknown }

// R-PRJ-05: what is planned to go in in one month (as a sub-category's plan, PLN-01).
public sealed record MonthlyAmount(string PeriodKey, long AmountMinor);

// R-PRJ-08: money needed on a day, such as a deposit.
public sealed record Milestone(string Id, string Label, DateOnly Date, long AmountMinor);

// PRD 4.11 and 7.4 project. Targets holds only the set ones, and ActiveTarget is one of them. DoneOn
// is null unless Done. StartPeriodKey is the month it was made in. Monthly has a month only when
// something is planned in it.
public sealed record Project(
    string Id,
    string Name,
    DateOnly Deadline,
    ProjectStatus Status,
    DateOnly? DoneOn,
    IReadOnlyDictionary<TargetLevel, long> Targets,
    TargetLevel ActiveTarget,
    string BankAccountId,
    IReadOnlyList<MonthlyAmount> Monthly,
    IReadOnlyList<Milestone> Milestones,
    string StartPeriodKey)
{
    public long Target => Targets[ActiveTarget];

    // R-PRJ-03: a project that is not Done holds its account.
    public bool HoldsAccount => Status != ProjectStatus.Done;
}

// R-PRJ-06: one month from the start month. Actual is null when the money at either end is unknown,
// and then the month carries nothing. Behind carries from month to month; below 0 it is ahead.
public sealed record ProjectMonth(Period Period, long Planned, long? Actual, long Behind);

// R-PRJ-07: a month after this one that starts by the deadline, with its monthly amount.
public sealed record ProjectMonthToCome(Period Period, long Amount);

// R-PRJ-08: Money is the real money at the start of the milestone's day when it has Passed, else the
// projected money; null when unknown (and for a Done project's milestones to come).
public sealed record MilestoneCheck(Milestone Milestone, bool Passed, long? Money)
{
    // Met or On track at 0 or more; Missed by or Short below 0.
    public long? Difference => Money - Milestone.AmountMinor;
}

// Everything PRJ-02 and PRJ-03 show, as of AsOf: today, or the day a Done project was done
// (R-PRJ-01). Null amounts are unknown (BNK-05). Projected is null after the deadline and for a Done
// project; Short is 0 when the projection reaches the target. A Paused project adds nothing more.
public sealed record ProjectFigures(
    DateOnly AsOf,
    Period? CurrentMonth,
    long? Money,
    long? Progress,
    long Target,
    long? StillToSave,
    int? Percent,
    IReadOnlyList<ProjectMonth> Months,
    long BehindBefore,
    long StillToPutIn,
    IReadOnlyList<ProjectMonthToCome> MonthsToCome,
    bool DeadlinePassed,
    long? Projected,
    long? Short,
    long? ExtraAMonth,
    int MonthsLeft,
    IReadOnlyList<MilestoneCheck> Milestones,
    ProjectState State)
{
    // R-PRJ-02: whether a target is reached by the deadline (by now once there is no projection).
    public bool? Covers(long target) => (Projected ?? Progress) is { } amount ? amount >= target : null;
}

// PRD 4.11 saving projects. Amounts are long minor units (R-MON-01). A project's money comes from its
// account's balance (R-PRJ-03): moneyAtStartOf(day) is it at the start of that day, before anything
// recorded that day, or null when it is unknown. Months must run, oldest first, from the
// start month through today, the deadline and every milestone (FiscalCalendar.MonthsThrough).
public static class ProjectCalculator
{
    private const long WholeUnit = 100;

    // R-PRJ-05: the month's amount; 0 when nothing is planned in it.
    public static long MonthlyFor(IEnumerable<MonthlyAmount> monthly, string periodKey) =>
        monthly.FirstOrDefault(amount => amount.PeriodKey == periodKey)?.AmountMinor ?? 0;

    // R-PRJ-05: the project's months, from its start month to the month that holds the deadline.
    public static IReadOnlyList<Period> PlanMonths(IReadOnlyList<Period> months, string startPeriodKey, DateOnly deadline) =>
        months.Where(period => string.CompareOrdinal(period.Key, startPeriodKey) >= 0 && period.StartDate <= deadline).ToList();

    // R-PRJ-06: a month's money is counted from its first day, or from the account's opening day when
    // the account was opened inside the month (nothing is known before it, R-BAL-01).
    public static DateOnly CountedFrom(Period period, DateOnly? openedOn) =>
        openedOn is { } opened && opened > period.StartDate && opened <= period.EndDate ? opened : period.StartDate;

    // R-PRJ-12: the same amount every month from this month to the deadline that brings the money at
    // the start of this month up to the target, rounded up to a whole unit; 0 once it is there. Null
    // while the money is unknown or no month is left.
    public static long? Recommended(long target, long? moneyAtMonthStart, int monthsLeft)
    {
        if (moneyAtMonthStart is not { } money || monthsLeft <= 0)
        {
            return null;
        }
        return target <= money ? 0 : RoundUpToUnit(CeilingDivide(target - money, monthsLeft));
    }

    public static ProjectFigures Figures(
        Project project, DateOnly today, IReadOnlyList<Period> months, Func<DateOnly, long?> moneyAtStartOf, DateOnly? openedOn = null)
    {
        var done = project.Status == ProjectStatus.Done;
        var asOf = done && project.DoneOn is { } doneOn ? doneOn : today;
        var current = months.FirstOrDefault(period => period.StartDate <= asOf && asOf <= period.EndDate);

        // R-PRJ-03, R-PRJ-04: money today is the money at the start of tomorrow, and it is the progress.
        var money = moneyAtStartOf(asOf.AddDays(1));
        var progress = money;
        var target = project.Target;

        // R-PRJ-06, from the start month to the month of AsOf.
        var rows = new List<ProjectMonth>();
        var behind = 0L;
        var behindBefore = 0L;
        if (current is not null)
        {
            foreach (var period in months.Where(period =>
                         string.CompareOrdinal(period.Key, project.StartPeriodKey) >= 0 && period.StartDate <= current.StartDate))
            {
                behindBefore = behind;
                var planned = MonthlyFor(project.Monthly, period.Key);
                var end = period == current ? asOf.AddDays(1) : period.EndDate.AddDays(1);
                var actual = moneyAtStartOf(end) - moneyAtStartOf(CountedFrom(period, openedOn));
                if (actual is { } amount)
                {
                    behind += planned - amount;
                }
                rows.Add(new ProjectMonth(period, planned, actual, behind));
            }
        }
        var thisMonth = rows.Count > 0 && rows[^1].Period == current ? rows[^1] : null;
        if (thisMonth is null)
        {
            behindBefore = behind;
        }
        // R-PRJ-01: a Paused project puts nothing more in.
        var stillToPutIn = thisMonth is null || project.Status == ProjectStatus.Paused ? 0 : Math.Max(0, thisMonth.Planned - (thisMonth.Actual ?? 0));

        // R-PRJ-07: each month after this one that starts by the deadline adds its monthly amount.
        var deadlinePassed = asOf > project.Deadline;
        var projects = !done && !deadlinePassed && current is not null;
        var adds = projects && project.Status == ProjectStatus.Active;
        IReadOnlyList<ProjectMonthToCome> monthsToCome = adds ? MonthsAfter(project, months, current!, project.Deadline) : [];
        var projected = projects ? progress + stillToPutIn + monthsToCome.Sum(month => month.Amount) : null;
        var shortBy = projected is { } total ? Math.Max(0, target - total) : (long?)null;
        var monthsLeft = projected is null ? 0 : 1 + monthsToCome.Count;
        long? extra = shortBy is > 0 ? RoundUpToUnit(CeilingDivide(shortBy.Value, monthsLeft)) : shortBy is null ? null : 0;

        // R-PRJ-08: real money on a passed day, else money today + this month's rest + months to that day.
        var milestones = project.Milestones
            .OrderBy(milestone => milestone.Date)
            .ThenBy(milestone => milestone.Label, StringComparer.OrdinalIgnoreCase)
            .Select(milestone => milestone.Date <= asOf
                ? new MilestoneCheck(milestone, true, moneyAtStartOf(milestone.Date))
                : new MilestoneCheck(milestone, false, done || current is null
                    ? null
                    : money + stillToPutIn + (adds ? MonthsAfter(project, months, current, milestone.Date).Sum(month => month.Amount) : 0)))
            .ToList();

        return new ProjectFigures(
            asOf,
            current,
            money,
            progress,
            target,
            progress is { } reached ? Math.Max(0, target - reached) : null,
            progress is { } part ? (int)Math.Max(0, part * 100 / target) : null,
            rows,
            behindBefore,
            stillToPutIn,
            monthsToCome,
            deadlinePassed,
            projected,
            shortBy,
            extra,
            monthsLeft,
            milestones,
            State(project, progress, projected, deadlinePassed, milestones));
    }

    // R-PRJ-10: the first that applies.
    private static ProjectState State(Project project, long? progress, long? projected, bool deadlinePassed, IReadOnlyList<MilestoneCheck> milestones)
    {
        if (project.Status == ProjectStatus.Done)
        {
            return ProjectState.Done;
        }
        if (project.Status == ProjectStatus.Paused)
        {
            return ProjectState.Paused;
        }
        if (progress is not { } amount)
        {
            return ProjectState.Unknown;
        }
        if (amount >= project.Target)
        {
            return ProjectState.Reached;
        }
        return deadlinePassed || projected < project.Target || milestones.Any(check => !check.Passed && check.Difference < 0)
            ? ProjectState.OffTrack
            : ProjectState.OnTrack;
    }

    // The months after this one that start on or before a day, each with its monthly amount (R-PRJ-05).
    private static IReadOnlyList<ProjectMonthToCome> MonthsAfter(Project project, IReadOnlyList<Period> months, Period current, DateOnly until) =>
        months.Where(period => period.StartDate > current.EndDate && period.StartDate <= until)
            .Select(period => new ProjectMonthToCome(period, MonthlyFor(project.Monthly, period.Key)))
            .ToList();

    private static long CeilingDivide(long amount, int parts) => (amount + parts - 1) / parts;

    // R-PRJ-07, R-PRJ-12: "Extra a month" and the recommended amount are rounded up to a whole unit.
    private static long RoundUpToUnit(long minor) => (minor + WholeUnit - 1) / WholeUnit * WholeUnit;
}
