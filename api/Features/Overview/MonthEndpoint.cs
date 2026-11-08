using System.Security.Claims;
using Api.Domain;
using Api.Features.BankAccounts;
using Api.Features.Entries;
using Api.Features.Income;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Overview;

// Owner note 9: GET /api/fiscal-years/{fyId}/months/{periodKey}, Tracker's one month in detail, next to
// the overview (Tracker makes these 2 calls, NFR-01): spending by day and by account, each category
// against the month before, the newest and the biggest entries, and the accounts for the edit dialog.
// Owner note 32: also what went over plan in the months before and is not made up yet (R-PLN-09),
// counted over the months back the owner picks (back, 1 by default).
// With Tracker's filter (categoryId, subCategoryId) every part covers only that scope.
public static class MonthEndpoint
{
    public record DayDto(DateOnly Date, decimal Spent);

    public record AccountDto(string BankAccountId, decimal Spent);

    public record CategoryDto(string CategoryId, decimal Spent, decimal LastSpent);

    // Owner note 32 (R-PLN-09): a category's overspend not made up yet, what is left of its plan this
    // month (planned - spent) and After = Left - Overspent, below 0 when this month cannot cover it.
    public record OverspentCategoryDto(string CategoryId, decimal Overspent, decimal Left, decimal After);

    // MonthsBack is the months counted: the picked number, at most the FY's months before this one (0
    // in its first month). From is the first of them. Total is every category's overspend (the month
    // card takes it off Remaining this month); Categories are only those with one, the shortest first.
    public record OverspentDto(int MonthsBack, MeEndpoint.PeriodDto? From, decimal Total, IReadOnlyList<OverspentCategoryDto> Categories);

    // LastMonth is null for the first stored month (its categories' LastSpent are then 0).
    // AdditionalIncome is the month's additional income items, whatever they count in (R-INC-04, owner
    // note 17), biggest first; empty with a filter, because income belongs to the whole budget.
    public record MonthResponse(
        MeEndpoint.PeriodDto Period,
        MeEndpoint.PeriodDto? LastMonth,
        decimal Total,
        int Count,
        IReadOnlyList<DayDto> Days,
        IReadOnlyList<AccountDto> Accounts,
        IReadOnlyList<CategoryDto> Categories,
        IReadOnlyList<EntriesEndpoint.EntryDto> Latest,
        IReadOnlyList<EntriesEndpoint.EntryDto> Biggest,
        IReadOnlyList<BankAccountsEndpoint.BankAccountDto> BankAccounts,
        IReadOnlyList<IncomeEndpoint.IncomeItemDto> AdditionalIncome,
        OverspentDto Overspent);

    public static IEndpointRouteBuilder MapMonth(this IEndpointRouteBuilder api)
    {
        // An unknown FY, or a month that is not one of its months, is 404.
        api.MapGet("/fiscal-years/{fyId}/months/{periodKey}", async (
            string fyId, string periodKey, string? categoryId, string? subCategoryId, int? back, ClaimsPrincipal user, IUserStore store) =>
            await LoadAsync(user.FindFirstValue("sub")!, fyId, periodKey, categoryId, subCategoryId, store, back) is { } month
                ? Results.Ok(month)
                : Results.NotFound());
        return api;
    }

    // Null for an unknown FY or month. Also the Claude connector's get_month (MCP-04).
    public static async Task<MonthResponse?> LoadAsync(
        string uid, string fyId, string periodKey, string? categoryId, string? subCategoryId, IUserStore store, int? back = null)
    {
        var fiscalYears = await store.GetFiscalYearsAsync(uid);
        var fiscalYear = fiscalYears.FirstOrDefault(item => item.Id == fyId);
        var period = fiscalYear?.Periods.FirstOrDefault(item => item.Key == periodKey);
        if (fiscalYear is null || period is null)
        {
            return null;
        }
        var scope = new EntryFilter(null, null, categoryId, subCategoryId, null, null);
        var fyEntries = await store.GetFiscalYearEntriesAsync(uid, fyId);
        var previous = FiscalCalendar.PreviousPeriod(fiscalYears, periodKey);
        // The month before is in this FY, except for its first month (one more query then).
        var previousEntries = previous is not { } found ? []
            : found.FiscalYear.Id == fyId ? fyEntries
            : await store.GetFiscalYearEntriesAsync(uid, found.FiscalYear.Id);

        var breakdown = MonthBreakdownCalculator.For(
            FiscalCalendar.Days(period),
            fyEntries.Where(entry => entry.PeriodKey == periodKey && scope.Matches(entry)).ToList(),
            previousEntries.Where(entry => entry.PeriodKey == previous?.Period.Key && scope.Matches(entry)).ToList());
        var overspent = await OverspentAsync(uid, fiscalYear, period, categoryId, subCategoryId, fyEntries.Where(scope.Matches).ToList(), back, store);

        return new MonthResponse(
            MeEndpoint.PeriodDto.From(period),
            previous is { } before ? MeEndpoint.PeriodDto.From(before.Period) : null,
            Money.FromMinor(breakdown.Total),
            breakdown.Count,
            breakdown.Days.Select(day => new DayDto(day.Date, Money.FromMinor(day.Spent))).ToList(),
            breakdown.Accounts.Select(account => new AccountDto(account.BankAccountId, Money.FromMinor(account.Spent))).ToList(),
            breakdown.Categories
                .Select(change => new CategoryDto(change.CategoryId, Money.FromMinor(change.Spent), Money.FromMinor(change.LastSpent)))
                .ToList(),
            breakdown.Latest.Select(EntriesEndpoint.EntryDto.From).ToList(),
            breakdown.Biggest.Select(EntriesEndpoint.EntryDto.From).ToList(),
            BankAccountsEndpoint.Sorted(await store.GetBankAccountsAsync(uid)),
            string.IsNullOrEmpty(categoryId) && string.IsNullOrEmpty(subCategoryId)
                ? (await store.GetIncomesAsync(uid))
                    .Where(item => item.Kind == IncomeKind.Additional && IncomeCalculator.IsActive(item, periodKey))
                    .OrderByDescending(item => item.AmountMinor)
                    .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                    .Select(IncomeEndpoint.IncomeItemDto.From)
                    .ToList()
                : [],
            overspent);
    }

    // R-PLN-09 (owner note 32): each sub-category in the filter on its own over the months back (the
    // picked number, 1 by default, within the FY), then added up by category. Left is the whole
    // category's plan this month minus its spending.
    private static async Task<OverspentDto> OverspentAsync(
        string uid, FiscalYear fiscalYear, Period period, string? categoryId, string? subCategoryId, IReadOnlyList<Entry> entries,
        int? back, IUserStore store)
    {
        var periods = fiscalYear.Periods.ToList();
        var index = periods.FindIndex(item => item.Key == period.Key);
        var monthsBack = Math.Min(Math.Max(back ?? 1, 1), index);
        var window = periods.Skip(index - monthsBack).Take(monthsBack).ToList();

        var plans = (await store.GetPlansAsync(uid, fiscalYear.Id))
            .Where(plan => (string.IsNullOrEmpty(categoryId) || plan.CategoryId == categoryId)
                && (string.IsNullOrEmpty(subCategoryId) || plan.SubCategoryId == subCategoryId))
            .ToDictionary(plan => plan.SubCategoryId);
        var spent = entries
            .GroupBy(entry => (entry.SubCategoryId, entry.PeriodKey))
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.AmountMinor));
        long Planned(string subCategoryId, string key) => plans.TryGetValue(subCategoryId, out var plan) ? PlanCalculator.AmountFor(plan, key) : 0;
        long Spent(string subCategoryId, string key) => spent.GetValueOrDefault((subCategoryId, key));

        var categories = plans.Values.Select(plan => (plan.SubCategoryId, plan.CategoryId))
            .Concat(entries.Select(entry => (entry.SubCategoryId, entry.CategoryId)))
            .DistinctBy(sub => sub.SubCategoryId)
            .GroupBy(sub => sub.CategoryId)
            .Select(group => (
                CategoryId: group.Key,
                Overspent: group.Sum(sub => PlanCalculator.Overspent(window.Select(month => (Planned(sub.SubCategoryId, month.Key), Spent(sub.SubCategoryId, month.Key))))),
                Left: group.Sum(sub => PlanCalculator.Remaining(Planned(sub.SubCategoryId, period.Key), Spent(sub.SubCategoryId, period.Key)))))
            .Where(category => category.Overspent > 0)
            .OrderBy(category => category.Left - category.Overspent)
            .ThenBy(category => category.CategoryId, StringComparer.Ordinal)
            .ToList();

        return new OverspentDto(
            monthsBack,
            monthsBack > 0 ? MeEndpoint.PeriodDto.From(window[0]) : null,
            Money.FromMinor(categories.Sum(category => category.Overspent)),
            categories.Select(category => new OverspentCategoryDto(
                category.CategoryId,
                Money.FromMinor(category.Overspent),
                Money.FromMinor(category.Left),
                Money.FromMinor(category.Left - category.Overspent))).ToList());
    }
}
