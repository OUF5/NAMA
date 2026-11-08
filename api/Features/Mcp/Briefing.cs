using Api.Domain;
using Api.Features.Loans;
using Api.Features.Overview;
using Api.Features.Profile;
using Api.Features.Projects;
using Api.Features.Summary;
using Api.Infrastructure;

namespace Api.Features.Mcp;

// Owner note 34 (MCP-04): get_briefing, the connector's "start here". One compact answer to "how am I
// doing?", with names instead of ids, made from what the pages read (the top bar's summary, Tracker's
// overview and month, Loans, Projects), so Claude and the app always agree. Every amount is a decimal
// in the profile's currency (R-MON-01).
public static class Briefing
{
    public const int ListSize = 5;

    // A plan line's status against the month's own plan (owner note 32, ENT-01): "withinPlan" below 80%,
    // "nearLimit" from 80% to 100%, "overPlan" above (spending with no plan is over plan).
    public const string WithinPlan = "withinPlan";
    public const string NearLimit = "nearLimit";
    public const string OverPlan = "overPlan";

    // The month in progress for the whole budget (owner note 32): RemainingThisMonth = Planned - Spent;
    // OverspentLastMonth is what went over plan last month and is not made up yet (R-PLN-09);
    // RemainingAfterOverspent = RemainingThisMonth - OverspentLastMonth. Income is the actual income,
    // AdditionalIncome its part from additional items.
    public record MonthBrief(
        MeEndpoint.PeriodDto Period,
        int Day,
        int Days,
        int DaysLeft,
        decimal Planned,
        decimal Spent,
        decimal RemainingThisMonth,
        string Status,
        decimal OverspentLastMonth,
        decimal RemainingAfterOverspent,
        decimal Income,
        decimal? AdditionalIncome,
        decimal? PlannedIncome);

    // A category, or a sub-category of it, this month: Spent of Planned with its Status;
    // LastMonthSpent for a category only.
    public record LineBrief(
        string Category,
        string? SubCategory,
        string CategoryId,
        string? SubCategoryId,
        decimal Spent,
        decimal Planned,
        string Status,
        decimal? LastMonthSpent);

    // R-PLN-09 over the last month: a category's overspend, what is left of its plan this month, and
    // After = Left - Overspent (below 0 when this month cannot cover it).
    public record OverspentBrief(string Category, string CategoryId, decimal Overspent, decimal LeftThisMonth, decimal AfterCovering);

    public record EntryBrief(DateOnly Date, string Category, string SubCategory, string Account, decimal Amount, string Note);

    public record AccountBrief(string Name, decimal? Balance);

    // R-BAL-03: Total is null until every active account has an opening balance and income a split.
    public record MoneyBrief(decimal? Total, bool Complete, IReadOnlyList<AccountBrief> Accounts);

    // The FY that contains today, from its first month (Dashboard, owner note 24): what came in and went
    // out so far against the plan, the saving so far against Plan's by now, and at the FY's end the
    // projected balance (R-PLN-07), Plan's saving and Tracker's projected saving. StillToSpendThisMonth
    // is what the projection still spends this month.
    public record OutlookBrief(
        DateOnly FiscalYearEnd,
        decimal PlannedIncomeSoFar,
        decimal IncomeSoFar,
        decimal PlannedSoFar,
        decimal SpentSoFar,
        decimal PlannedSavedByNow,
        decimal SavedSoFar,
        decimal StillToSpendThisMonth,
        decimal? ProjectedBalanceAtFyEnd,
        decimal PlannedSavingAtFyEnd,
        decimal ProjectedSavingAtFyEnd,
        decimal SavingVsPlanAtFyEnd);

    // An additional income item in this month or the next (R-INC-03): Counts is "planned", "actual" or
    // "both" (R-INC-04), Account null for a Planned item.
    public record IncomeBrief(string Label, string Month, decimal Amount, string Counts, string? Account);

    // R-LON-04: the open loans (information only, they change no balance).
    public record LoansBrief(decimal LentOpen, int LentOpenCount, decimal BorrowedOpen, int BorrowedOpenCount, decimal Net);

    // A saving project that is not Done (PRJ-02): State is "onTrack", "offTrack", "reached" or "paused";
    // Money is its account's balance (null when unknown); Short or ExtraAMonth say how far the forecast
    // falls short of the target, and what more a month closes it.
    public record ProjectBrief(
        string Name,
        string State,
        decimal Target,
        decimal? Money,
        int? Percent,
        DateOnly Deadline,
        int MonthsLeft,
        decimal? StillToSave,
        decimal StillToPutInThisMonth,
        decimal? Short,
        decimal? ExtraAMonth);

    public record BriefingResult(
        DateOnly Today,
        string Currency,
        string? FiscalYear,
        string? FyId,
        MonthBrief? Month,
        IReadOnlyList<LineBrief> Watch,
        IReadOnlyList<LineBrief> TopCategories,
        IReadOnlyList<OverspentBrief> OverspentToMakeUp,
        IReadOnlyList<EntryBrief> BiggestThisMonth,
        IReadOnlyList<EntryBrief> LatestThisMonth,
        MoneyBrief Money,
        OutlookBrief? Outlook,
        IReadOnlyList<IncomeBrief> AdditionalIncomeSoon,
        LoansBrief Loans,
        IReadOnlyList<ProjectBrief> Projects);

    // Null when the user has not finished setup.
    public static async Task<BriefingResult?> LoadAsync(string uid, IUserStore store, TimeProvider time)
    {
        var me = await MeEndpoint.LoadAsync(uid, isAdmin: false, store, time);
        if (!me.SetupCompleted || me.Profile is null || me.Today is not { } today)
        {
            return null;
        }
        var fyId = me.CurrentFyId;
        var periodKey = me.CurrentPeriodKey;
        // The parts are read at the same time.
        var summaryRead = SummaryEndpoint.LoadAsync(uid, store, time);
        var loansRead = LoansEndpoint.LoadAsync(uid, store, time);
        var projectsRead = ProjectsEndpoint.LoadAsync(uid, store, time);
        var incomesRead = store.GetIncomesAsync(uid);
        var overviewRead = fyId is null
            ? Task.FromResult<OverviewEndpoint.OverviewResponse?>(null)
            : OverviewEndpoint.LoadAsync(uid, fyId, null, null, store, time);
        var monthRead = fyId is null || periodKey is null
            ? Task.FromResult<MonthEndpoint.MonthResponse?>(null)
            : MonthEndpoint.LoadAsync(uid, fyId, periodKey, null, null, store);
        await Task.WhenAll(summaryRead, loansRead, projectsRead, incomesRead, overviewRead, monthRead);
        var summary = (await summaryRead)!;
        var loans = (await loansRead)!;
        var projects = await projectsRead;
        var incomes = await incomesRead;
        var overview = await overviewRead;
        var month = await monthRead;

        var categoryNames = overview?.Categories.ToDictionary(category => category.Id, category => category.Name) ?? [];
        var subNames = overview?.Categories.SelectMany(category => category.SubCategories).ToDictionary(sub => sub.Id, sub => sub.Name) ?? [];
        var accountNames = loans.Accounts.ToDictionary(account => account.Id, account => account.Name);
        string Category(string id) => categoryNames.GetValueOrDefault(id, "");
        string Account(string? id) => id is null ? "" : accountNames.GetValueOrDefault(id, "");

        var index = overview?.Months.ToList().FindIndex(item => item.Period.Key == periodKey) ?? -1;
        var lastSpent = month?.Categories.ToDictionary(change => change.CategoryId, change => change.LastSpent) ?? [];
        var watch = index < 0 ? [] : overview!.Rows
            .SelectMany(row => row.SubCategories.Select(sub => Line(row.CategoryId, sub.SubCategoryId, sub.Months[index], null)))
            .Where(line => line.Status is NearLimit or OverPlan)
            .OrderBy(line => line.Status == OverPlan ? 0 : 1)
            .ThenByDescending(line => line.Planned == 0 ? decimal.MaxValue : line.Spent / line.Planned)
            .Take(ListSize)
            .ToList();
        var topCategories = index < 0 ? [] : overview!.Rows
            .Select(row => Line(row.CategoryId, null, row.Months[index], lastSpent.TryGetValue(row.CategoryId, out var last) ? last : 0))
            .Where(line => line.Spent > 0)
            .OrderByDescending(line => line.Spent)
            .Take(ListSize)
            .ToList();

        LineBrief Line(string categoryId, string? subCategoryId, OverviewEndpoint.CellDto cell, decimal? lastMonthSpent) => new(
            Category(categoryId),
            subCategoryId is null ? null : subNames.GetValueOrDefault(subCategoryId, ""),
            categoryId,
            subCategoryId,
            cell.Spent,
            cell.Planned,
            Status(cell.Planned, cell.Spent),
            lastMonthSpent);

        EntryBrief Entry(Entries.EntriesEndpoint.EntryDto entry) => new(
            entry.Date, Category(entry.CategoryId), subNames.GetValueOrDefault(entry.SubCategoryId, ""), Account(entry.BankAccountId), entry.Amount, entry.Note);

        var current = summary.Month is { } now && now.Period.Key == periodKey ? now : null;
        var monthOverview = index < 0 ? null : overview!.Months[index];
        MonthBrief? monthBrief = current is null ? null : new MonthBrief(
            current.Period,
            current.Day,
            current.Days,
            current.DaysLeft,
            current.Planned,
            current.Spent,
            current.Planned - current.Spent,
            Status(current.Planned, current.Spent),
            current.OverspentLastMonth,
            current.Planned - current.Spent - current.OverspentLastMonth,
            current.Income,
            monthOverview?.AdditionalIncome,
            monthOverview?.PlannedIncome);

        var vs = overview?.PlanVsActual;
        OutlookBrief? outlook = vs is null || summary.Projection is not { } projection ? null : new OutlookBrief(
            projection.EndDate,
            vs.PlannedIncome,
            vs.Income,
            vs.Planned,
            vs.Spent,
            vs.PlannedSaved,
            vs.Saved,
            vs.StillToSpend,
            projection.ProjectedBalance,
            vs.PlannedSavedAtEnd,
            vs.ProjectedSaved,
            vs.ProjectedSaved - vs.PlannedSavedAtEnd);

        // This month and the next, as the FY has them.
        var soon = index < 0 ? [] : overview!.Months.Skip(index).Take(2).Select(item => item.Period).ToList();
        var incomeSoon = soon
            .SelectMany(period => incomes
                .Where(item => item.Kind == IncomeKind.Additional && IncomeCalculator.IsActive(item, period.Key))
                .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .Select(item => new IncomeBrief(
                    item.Label, period.Label, Money.FromMinor(item.AmountMinor), FirestoreDocuments.ToCountsName(item.Counts),
                    item.BankAccountId is null ? null : Account(item.BankAccountId))))
            .ToList();

        return new BriefingResult(
            today,
            me.Profile.Currency,
            overview?.FiscalYears.FirstOrDefault(fiscalYear => fiscalYear.Id == fyId)?.Label,
            fyId,
            monthBrief,
            watch,
            topCategories,
            month?.Overspent.Categories
                .Select(category => new OverspentBrief(Category(category.CategoryId), category.CategoryId, category.Overspent, category.Left, category.After))
                .ToList() ?? [],
            month?.Biggest.Select(Entry).ToList() ?? [],
            month?.Latest.Select(Entry).ToList() ?? [],
            new MoneyBrief(summary.Balance, summary.Complete, summary.Accounts.Select(account => new AccountBrief(account.Name, account.Balance)).ToList()),
            outlook,
            incomeSoon,
            new LoansBrief(
                loans.Totals.Lent.Open, loans.Totals.Lent.OpenCount, loans.Totals.Borrowed.Open, loans.Totals.Borrowed.OpenCount, loans.Totals.Net),
            projects?.Projects
                .Where(project => project.State != ProjectsEndpoint.ToStateName(ProjectState.Done))
                .Select(project => new ProjectBrief(
                    project.Name, project.State, project.Target, project.Money, project.Percent, project.Deadline, project.MonthsLeft,
                    project.StillToSave, project.StillToPutIn, project.Short, project.ExtraAMonth))
                .ToList() ?? []);
    }

    public static string Status(decimal planned, decimal spent) =>
        spent > planned ? OverPlan : planned > 0 && spent * 100 >= planned * 80 ? NearLimit : WithinPlan;
}
