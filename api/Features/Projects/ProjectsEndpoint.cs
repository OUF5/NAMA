using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.Banks;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Projects;

// PRD 5.14 Projects (owner note 30): saving goals read from the bank balances (R-PRJ-03). One call
// serves the list and a project (PRJ-02, PRJ-03), and every change answers with it (NFR-01).
public static class ProjectsEndpoint
{
    // R-PRJ-02: a project is no further away than this, so its months stay few.
    public const int MaxYearsAhead = 50;

    public const int MaxMilestones = 20;

    // Covered is whether the projection reaches it by the deadline (by now once nothing is projected).
    public record TargetDto(string Level, decimal Amount, bool Active, bool? Covered);

    // R-PRJ-06: Actual is null when unknown; Behind below 0 is ahead.
    public record MonthDto(MeEndpoint.PeriodDto Period, decimal Planned, decimal? Actual, decimal Behind);

    // R-PRJ-05, R-PRJ-07: a month with its planned amount.
    public record MonthToComeDto(MeEndpoint.PeriodDto Period, decimal Amount);

    // R-PRJ-08: Money is real once Passed, else projected; Difference at 0 or more is Met or On track.
    public record MilestoneDto(string Id, string Label, DateOnly Date, decimal Amount, bool Passed, decimal? Money, decimal? Difference);

    // PRJ-02, PRJ-03. MonthlyAmount is the amount of the project's month (today's, or its done day's);
    // Plan is every month from the start month to the deadline with its amount (R-PRJ-05). State is
    // R-PRJ-10's. Everything after State is as of AsOf (R-PRJ-01: a Done project's day).
    public record ProjectDto(
        string Id,
        string Name,
        DateOnly Deadline,
        string Status,
        DateOnly? DoneOn,
        string StartPeriodKey,
        IReadOnlyList<TargetDto> Targets,
        string ActiveTarget,
        string BankAccountId,
        decimal MonthlyAmount,
        IReadOnlyList<MonthToComeDto> Plan,
        string State,
        DateOnly AsOf,
        MeEndpoint.PeriodDto? CurrentMonth,
        decimal? Money,
        decimal? Progress,
        decimal Target,
        decimal? StillToSave,
        int? Percent,
        IReadOnlyList<MonthDto> Months,
        decimal BehindBefore,
        decimal StillToPutIn,
        IReadOnlyList<MonthToComeDto> MonthsToCome,
        bool DeadlinePassed,
        decimal? Projected,
        decimal? Short,
        decimal? ExtraAMonth,
        int MonthsLeft,
        IReadOnlyList<MilestoneDto> Milestones);

    // The dialog's account picker (PRJ-04): the active accounts, and archived ones a project still
    // counts. Balance is today's (null without an opening balance); ProjectId is the project that is
    // not Done counting it.
    public record AccountDto(string Id, string Name, bool Archived, decimal? Balance, string? ProjectId);

    // CurrentMonth is today's month (stored, or as the next FY would have it).
    public record ProjectsResponse(
        DateOnly Today,
        MeEndpoint.PeriodDto? CurrentMonth,
        IReadOnlyList<ProjectDto> Projects,
        IReadOnlyList<AccountDto> Accounts);

    // PRJ-04: the dialog's months, from the start month (this month for a new project) to the month
    // that holds the deadline, and the recommended amount a month (R-PRJ-12; null until the account
    // and the Likely target are given, or while the account's money is unknown).
    public record ProjectMonthsResponse(IReadOnlyList<MeEndpoint.PeriodDto> Months, string CurrentKey, decimal? Recommended);

    public static IEndpointRouteBuilder MapProjects(this IEndpointRouteBuilder api)
    {
        api.MapGet("/projects", (ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
            ResultAsync(user.FindFirstValue("sub")!, store, time));

        api.MapGet("/projects/months", async (DateOnly deadline, string? bankAccountId, decimal? likely, string? projectId,
            ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (likely is { } amount && !Money.IsValidAmount(amount))
            {
                return Invalid("Likely", "A target is more than 0 and at most 10,000,000, with at most 2 decimals.");
            }
            if (await BanksEndpoint.LoadAsync(uid, store, time) is not { } data)
            {
                return Results.NotFound();
            }
            if (deadline > data.Today.AddYears(MaxYearsAhead))
            {
                return Invalid("Deadline", $"The deadline is at most {MaxYearsAhead} years away.");
            }
            Project? project = null;
            if (projectId is not null && (project = await store.GetProjectAsync(uid, projectId)) is null)
            {
                return Results.NotFound();
            }
            var months = Months(data, deadline > data.Today ? deadline : data.Today);
            if (months.FirstOrDefault(period => period.StartDate <= data.Today && data.Today <= period.EndDate) is not { } thisMonth)
            {
                return Results.NotFound();
            }
            var plan = ProjectCalculator.PlanMonths(months, project?.StartPeriodKey ?? thisMonth.Key, deadline);
            var monthsLeft = plan.Count(period => string.CompareOrdinal(period.Key, thisMonth.Key) >= 0);
            var recommended = likely is { } target && bankAccountId is not null
                ? ProjectCalculator.Recommended(Money.ToMinor(target),
                    MoneyAtStartOf(data, bankAccountId, ProjectCalculator.CountedFrom(thisMonth, OpenedOn(data, bankAccountId))), monthsLeft)
                : null;
            return Results.Ok(new ProjectMonthsResponse(
                plan.Select(MeEndpoint.PeriodDto.From).ToList(),
                thisMonth.Key,
                recommended is { } minor ? Money.FromMinor(minor) : null));
        });

        api.MapPost("/projects", async (ProjectRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var (project, error) = await BuildAsync(uid, DocumentIds.New(), request, current: null, store, time);
            if (project is null)
            {
                return error!;
            }
            await store.AddProjectAsync(uid, project);
            return await ResultAsync(uid, store, time, created: true);
        });

        // An unknown project, or another user's, is 404. Status and the done day stay (PUT …/status).
        api.MapPut("/projects/{id}", async (string id, ProjectRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (await store.GetProjectAsync(uid, id) is not { } current)
            {
                return Results.NotFound();
            }
            var (project, error) = await BuildAsync(uid, id, request, current, store, time);
            if (project is null)
            {
                return error!;
            }
            return await store.UpdateProjectAsync(uid, project) ? await ResultAsync(uid, store, time) : Results.NotFound();
        });

        // R-PRJ-01: Pause, Resume, Mark done, Reopen.
        api.MapPut("/projects/{id}/status", async (string id, ProjectStatusRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (await store.GetProjectAsync(uid, id) is not { } project)
            {
                return Results.NotFound();
            }
            var (changed, error) = await ChangeStatusAsync(uid, project, request, store, time);
            if (changed is null)
            {
                return error!;
            }
            return await store.UpdateProjectAsync(uid, changed) ? await ResultAsync(uid, store, time) : Results.NotFound();
        });

        // The confirmation is in the web app (UX-04). Its expenses stay, without the project.
        api.MapDelete("/projects/{id}", async (string id, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            return await store.DeleteProjectAsync(uid, id) ? await ResultAsync(uid, store, time) : Results.NotFound();
        });

        return api;
    }

    public static async Task<IResult> ResultAsync(string uid, IUserStore store, TimeProvider time, bool created = false)
    {
        if (await LoadAsync(uid, store, time) is not { } response)
        {
            return Results.NotFound();
        }
        return created ? Results.Created("/api/projects", response) : Results.Ok(response);
    }

    // Null when the user has no profile. The balances are read as Banks reads them (BanksEndpoint).
    public static async Task<ProjectsResponse?> LoadAsync(string uid, IUserStore store, TimeProvider time)
    {
        if (await BanksEndpoint.LoadAsync(uid, store, time) is not { } data)
        {
            return null;
        }
        var projects = await store.GetProjectsAsync(uid);
        var until = projects
            .SelectMany(project => project.Milestones.Select(milestone => milestone.Date).Append(project.Deadline))
            .Append(data.Today)
            .Max();
        var months = Months(data, until);
        var current = months.FirstOrDefault(period => period.StartDate <= data.Today && data.Today <= period.EndDate);

        var dtos = projects
            .OrderBy(project => project.Status == ProjectStatus.Done)
            .ThenBy(project => project.Status == ProjectStatus.Paused)
            .ThenBy(project => project.Deadline)
            .Select(project => ToDto(project, data, months,
                ProjectCalculator.Figures(
                    project,
                    data.Today,
                    months,
                    day => MoneyAtStartOf(data, project.BankAccountId, day),
                    OpenedOn(data, project.BankAccountId))))
            .ToList();

        var holders = projects.Where(project => project.HoldsAccount)
            .GroupBy(project => project.BankAccountId)
            .ToDictionary(group => group.Key, group => group.First().Id);
        var accounts = data.Accounts
            .Where(account => !account.Archived || holders.ContainsKey(account.Id))
            .OrderBy(account => Domain.Names.Lower(account.Name), StringComparer.Ordinal)
            .Select(account => new AccountDto(
                account.Id,
                account.Name,
                account.Archived,
                data.Balances.TryGetValue(account.Id, out var balance) ? Money.FromMinor(balance.Balance) : null,
                holders.GetValueOrDefault(account.Id)))
            .ToList();

        return new ProjectsResponse(
            data.Today,
            current is null ? null : MeEndpoint.PeriodDto.From(current),
            dtos,
            accounts);
    }

    // R-PRJ-03: the account's balance at the start of a day, before anything recorded that day (the end
    // of the day before, R-BAL-03). Unknown when it has no opening balance, or the day is before its
    // opening date.
    private static long? MoneyAtStartOf(BalanceData data, string accountId, DateOnly day)
    {
        if (data.Accounts.FirstOrDefault(account => account.Id == accountId) is not { } account
            || account.Opening is not { } opening || opening.Date > day)
        {
            return null;
        }
        return data.BalanceOn(account, day.AddDays(-1)).Balance;
    }

    private static DateOnly? OpenedOn(BalanceData data, string accountId) =>
        data.Accounts.FirstOrDefault(account => account.Id == accountId)?.Opening?.Date;

    // The stored months and the ones after them through a day (FiscalCalendar.MonthsThrough).
    private static IReadOnlyList<Period> Months(BalanceData data, DateOnly until) =>
        FiscalCalendar.MonthsThrough(data.FiscalYears, until, data.Profile.FiscalStartMonth, data.Profile.PaydayDay, data.Profile.MonthNaming);

    private static ProjectDto ToDto(Project project, BalanceData data, IReadOnlyList<Period> months, ProjectFigures figures)
    {
        decimal? Money(long? minor) => minor is { } value ? Domain.Money.FromMinor(value) : null;
        var monthKey = figures.CurrentMonth?.Key ?? project.StartPeriodKey;
        return new ProjectDto(
            project.Id,
            project.Name,
            project.Deadline,
            FirestoreDocuments.ToProjectStatusName(project.Status),
            project.DoneOn,
            project.StartPeriodKey,
            new[] { TargetLevel.Best, TargetLevel.Likely, TargetLevel.Worst }
                .Where(project.Targets.ContainsKey)
                .Select(level => new TargetDto(
                    FirestoreDocuments.ToTargetName(level),
                    Domain.Money.FromMinor(project.Targets[level]),
                    level == project.ActiveTarget,
                    figures.Covers(project.Targets[level])))
                .ToList(),
            FirestoreDocuments.ToTargetName(project.ActiveTarget),
            project.BankAccountId,
            Domain.Money.FromMinor(ProjectCalculator.MonthlyFor(project.Monthly, monthKey)),
            ProjectCalculator.PlanMonths(months, project.StartPeriodKey, project.Deadline)
                .Select(period => new MonthToComeDto(MeEndpoint.PeriodDto.From(period), Domain.Money.FromMinor(ProjectCalculator.MonthlyFor(project.Monthly, period.Key))))
                .ToList(),
            ToStateName(figures.State),
            figures.AsOf,
            figures.CurrentMonth is { } current ? MeEndpoint.PeriodDto.From(current) : null,
            Money(figures.Money),
            Money(figures.Progress),
            Domain.Money.FromMinor(figures.Target),
            Money(figures.StillToSave),
            figures.Percent,
            figures.Months
                .Select(month => new MonthDto(MeEndpoint.PeriodDto.From(month.Period), Domain.Money.FromMinor(month.Planned), Money(month.Actual),
                    Domain.Money.FromMinor(month.Behind)))
                .ToList(),
            Domain.Money.FromMinor(figures.BehindBefore),
            Domain.Money.FromMinor(figures.StillToPutIn),
            figures.MonthsToCome.Select(month => new MonthToComeDto(MeEndpoint.PeriodDto.From(month.Period), Domain.Money.FromMinor(month.Amount))).ToList(),
            figures.DeadlinePassed,
            Money(figures.Projected),
            Money(figures.Short),
            Money(figures.ExtraAMonth),
            figures.MonthsLeft,
            figures.Milestones
                .Select(check => new MilestoneDto(check.Milestone.Id, check.Milestone.Label, check.Milestone.Date,
                    Domain.Money.FromMinor(check.Milestone.AmountMinor), check.Passed, Money(check.Money), Money(check.Difference)))
                .ToList());
    }

    public static string ToStateName(ProjectState state) => state switch
    {
        ProjectState.OnTrack => "onTrack",
        ProjectState.OffTrack => "offTrack",
        ProjectState.Reached => "reached",
        ProjectState.Paused => "paused",
        ProjectState.Done => "done",
        _ => "unknown",
    };

    // R-PRJ-01 to R-PRJ-12. A new project starts in today's month; its amounts are for months from its
    // start month to the month that holds the deadline (R-PRJ-05).
    private static async Task<(Project? Project, IResult? Error)> BuildAsync(
        string uid, string id, ProjectRequest request, Project? current, IUserStore store, TimeProvider time)
    {
        var profile = (await store.GetProfileAsync(uid))!;
        var today = FiscalCalendar.Today(time, profile.Timezone);
        if (request.Deadline!.Value > today.AddYears(MaxYearsAhead))
        {
            return (null, Invalid(nameof(ProjectRequest.Deadline), $"The deadline is at most {MaxYearsAhead} years away."));
        }
        var projects = await store.GetProjectsAsync(uid);
        var name = Domain.Names.Normalize(request.Name);
        if (projects.Any(project => project.Id != id && Domain.Names.Lower(project.Name) == Domain.Names.Lower(name)))
        {
            return (null, Invalid(nameof(ProjectRequest.Name), "A project with this name already exists."));
        }

        // R-PRJ-03: the picker hides archived accounts, except the one the project already counts.
        var accountId = request.BankAccountId!;
        var accounts = await store.GetBankAccountsAsync(uid);
        if (!accounts.Any(account => account.Id == accountId && (!account.Archived || current?.BankAccountId == accountId)))
        {
            return (null, Invalid(nameof(ProjectRequest.BankAccountId), "Bank account is unknown."));
        }
        var status = current?.Status ?? ProjectStatus.Active;
        if (status != ProjectStatus.Done && TakenBy(projects, id, accountId, accounts) is { } taken)
        {
            return (null, taken);
        }

        var deadline = request.Deadline.Value;
        var months = FiscalCalendar.MonthsThrough(await store.GetFiscalYearsAsync(uid), deadline > today ? deadline : today,
            profile.FiscalStartMonth, profile.PaydayDay, profile.MonthNaming);
        var thisMonth = months.FirstOrDefault(period => period.StartDate <= today && today <= period.EndDate);
        if (thisMonth is null)
        {
            return (null, Results.NotFound());
        }
        var start = current?.StartPeriodKey ?? thisMonth.Key;
        var planKeys = ProjectCalculator.PlanMonths(months, start, deadline).Select(period => period.Key).ToHashSet();
        var amounts = request.Monthly ?? [];
        if (amounts.Any(amount => !planKeys.Contains(amount.PeriodKey!)))
        {
            return (null, Invalid(nameof(ProjectRequest.Monthly), "Amounts are for months from the project's start to its deadline."));
        }
        // An empty month is 0, so only months with an amount are kept.
        IReadOnlyList<MonthlyAmount> monthly = amounts
            .Where(amount => amount.Amount > 0)
            .Select(amount => new MonthlyAmount(amount.PeriodKey!, Money.ToMinor(amount.Amount!.Value)))
            .OrderBy(amount => amount.PeriodKey, StringComparer.Ordinal)
            .ToList();

        var targets = new Dictionary<TargetLevel, long>();
        foreach (var (level, value) in new[] { (TargetLevel.Best, request.Best), (TargetLevel.Likely, request.Likely), (TargetLevel.Worst, request.Worst) })
        {
            if (value is { } target)
            {
                targets[level] = Money.ToMinor(target);
            }
        }
        var milestones = (request.Milestones ?? [])
            .Select(milestone => new Milestone(
                milestone.Id is { } known && current?.Milestones.Any(item => item.Id == known) == true ? known : DocumentIds.New(),
                Domain.Names.Normalize(milestone.Label),
                milestone.Date!.Value,
                Money.ToMinor(milestone.Amount!.Value)))
            .ToList();

        return (new Project(
            id,
            name,
            deadline,
            status,
            current?.DoneOn,
            targets,
            FirestoreDocuments.ToTargetLevel(request.ActiveTarget),
            accountId,
            monthly,
            milestones,
            start), null);
    }

    // R-PRJ-01: Pause and Resume keep the amounts (a Paused project adds nothing more), Mark done keeps
    // the figures of its day and frees the account, Reopen takes it back when no other project has.
    private static async Task<(Project? Project, IResult? Error)> ChangeStatusAsync(
        string uid, Project project, ProjectStatusRequest request, IUserStore store, TimeProvider time)
    {
        var profile = (await store.GetProfileAsync(uid))!;
        var today = FiscalCalendar.Today(time, profile.Timezone);
        var months = FiscalCalendar.MonthsThrough(await store.GetFiscalYearsAsync(uid), today, profile.FiscalStartMonth, profile.PaydayDay, profile.MonthNaming);
        var status = FirestoreDocuments.ToProjectStatus(request.Status);
        if (status == ProjectStatus.Done)
        {
            var start = months.FirstOrDefault(period => period.Key == project.StartPeriodKey)?.StartDate ?? DateOnly.MinValue;
            if (request.DoneOn is not { } doneOn || doneOn > today || doneOn < start)
            {
                return (null, Invalid(nameof(ProjectStatusRequest.DoneOn), "Pick a day from the project's start month to today."));
            }
            return (project with { Status = status, DoneOn = doneOn }, null);
        }
        if (project.Status == ProjectStatus.Done
            && TakenBy(await store.GetProjectsAsync(uid), project.Id, project.BankAccountId, await store.GetBankAccountsAsync(uid)) is { } taken)
        {
            return (null, taken);
        }
        return (project with { Status = status, DoneOn = null }, null);
    }

    // R-PRJ-03: 409 when another project that is not Done counts the account.
    private static IResult? TakenBy(IReadOnlyList<Project> projects, string id, string accountId, IReadOnlyList<BankAccount> accounts)
    {
        if (projects.FirstOrDefault(project => project.Id != id && project.HoldsAccount && project.BankAccountId == accountId) is not { } other)
        {
            return null;
        }
        var name = accounts.FirstOrDefault(account => account.Id == accountId)?.Name ?? "This account";
        return Results.Problem(
            detail: $"{name} is already counted by {other.Name}.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["bankAccountId"] = accountId, ["projectName"] = other.Name });
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

// R-PRJ-01 to R-PRJ-12. ActiveTarget is "best", "likely" or "worst" and must be set. Monthly is the
// amount of each month from the start month to the deadline (R-PRJ-05); a month left out is 0.
public sealed class ProjectRequest : IValidatableObject
{
    public string? Name { get; init; }

    [Required]
    public DateOnly? Deadline { get; init; }

    public decimal? Best { get; init; }

    public decimal? Likely { get; init; }

    public decimal? Worst { get; init; }

    [Required]
    [AllowedValues("best", "likely", "worst")]
    public string? ActiveTarget { get; init; }

    [Required]
    public string? BankAccountId { get; init; }

    public List<MonthlyAmountRequest>? Monthly { get; init; }

    public List<MilestoneRequest>? Milestones { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Domain.Names.IsValid(Name))
        {
            yield return new ValidationResult("Name is 1 to 40 characters.", [nameof(Name)]);
        }
        // R-PRJ-02: at least one target, each R-MON-02, and the active one is set.
        var targets = new Dictionary<string, decimal?> { ["best"] = Best, ["likely"] = Likely, ["worst"] = Worst };
        foreach (var (level, value) in targets)
        {
            if (value is { } amount && !Money.IsValidAmount(amount))
            {
                yield return new ValidationResult("A target is more than 0 and at most 10,000,000, with at most 2 decimals.",
                    [char.ToUpperInvariant(level[0]) + level[1..]]);
            }
        }
        if (targets.Values.All(value => value is null))
        {
            yield return new ValidationResult("Set at least one target.", ["Targets"]);
        }
        else if (ActiveTarget is { } active && targets.TryGetValue(active, out var chosen) && chosen is null)
        {
            yield return new ValidationResult("The active target is one that is set.", [nameof(ActiveTarget)]);
        }
        // R-PRJ-05: each month once, 0 to 10,000,000.
        var monthly = Monthly ?? [];
        if (monthly.Count > ProjectsEndpoint.MaxYearsAhead * 13
            || monthly.Select(amount => amount.PeriodKey).Distinct(StringComparer.Ordinal).Count() != monthly.Count)
        {
            yield return new ValidationResult("Give each month once.", [nameof(Monthly)]);
        }
        for (var i = 0; i < monthly.Count; i++)
        {
            if (string.IsNullOrEmpty(monthly[i].PeriodKey))
            {
                yield return new ValidationResult("Pick a month.", [$"{nameof(Monthly)}[{i}].PeriodKey"]);
            }
            if (monthly[i].Amount is not { } amount || !Money.IsValidAmount(amount, allowZero: true))
            {
                yield return new ValidationResult("Amount is 0 to 10,000,000, with at most 2 decimals.", [$"{nameof(Monthly)}[{i}].Amount"]);
            }
        }
        // R-PRJ-08
        var milestones = Milestones ?? [];
        if (milestones.Count > ProjectsEndpoint.MaxMilestones)
        {
            yield return new ValidationResult($"A project has at most {ProjectsEndpoint.MaxMilestones} milestones.", [nameof(Milestones)]);
        }
        for (var i = 0; i < milestones.Count; i++)
        {
            var milestone = milestones[i];
            if (!Domain.Names.IsValid(milestone.Label))
            {
                yield return new ValidationResult("Label is 1 to 40 characters.", [$"{nameof(Milestones)}[{i}].Label"]);
            }
            if (milestone.Date is null)
            {
                yield return new ValidationResult("Pick a date.", [$"{nameof(Milestones)}[{i}].Date"]);
            }
            if (milestone.Amount is not { } amount || !Money.IsValidAmount(amount))
            {
                yield return new ValidationResult("Amount is more than 0 and at most 10,000,000, with at most 2 decimals.", [$"{nameof(Milestones)}[{i}].Amount"]);
            }
        }
    }
}

// R-PRJ-05: one month's amount.
public sealed class MonthlyAmountRequest
{
    public string? PeriodKey { get; init; }

    public decimal? Amount { get; init; }
}

// R-PRJ-08. Id is a milestone the project already has, or empty for a new one.
public sealed class MilestoneRequest
{
    public string? Id { get; init; }

    public string? Label { get; init; }

    public DateOnly? Date { get; init; }

    public decimal? Amount { get; init; }
}

// R-PRJ-01: "active" resumes or reopens; "done" needs DoneOn.
public sealed class ProjectStatusRequest
{
    [Required]
    [AllowedValues("active", "paused", "done")]
    public string? Status { get; init; }

    public DateOnly? DoneOn { get; init; }
}
