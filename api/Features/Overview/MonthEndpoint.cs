using System.Security.Claims;
using Api.Domain;
using Api.Features.BankAccounts;
using Api.Features.Entries;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Overview;

// Owner note 9: GET /api/fiscal-years/{fyId}/months/{periodKey}, Tracker's one month in detail, next to
// the overview (Tracker makes these 2 calls, NFR-01): spending by day and by account, each category
// against the month before, the newest and the biggest entries, and the accounts for the edit dialog.
// With Tracker's filter (categoryId, subCategoryId) every part covers only that scope.
public static class MonthEndpoint
{
    public record DayDto(DateOnly Date, decimal Spent);

    public record AccountDto(string BankAccountId, decimal Spent);

    public record CategoryDto(string CategoryId, decimal Spent, decimal LastSpent);

    // LastMonth is null for the first stored month (its categories' LastSpent are then 0).
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
        IReadOnlyList<BankAccountsEndpoint.BankAccountDto> BankAccounts);

    public static IEndpointRouteBuilder MapMonth(this IEndpointRouteBuilder api)
    {
        // An unknown FY, or a month that is not one of its months, is 404.
        api.MapGet("/fiscal-years/{fyId}/months/{periodKey}", async (
            string fyId, string periodKey, string? categoryId, string? subCategoryId, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var fiscalYears = await store.GetFiscalYearsAsync(uid);
            var fiscalYear = fiscalYears.FirstOrDefault(item => item.Id == fyId);
            var period = fiscalYear?.Periods.FirstOrDefault(item => item.Key == periodKey);
            if (fiscalYear is null || period is null)
            {
                return Results.NotFound();
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

            return Results.Ok(new MonthResponse(
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
                BankAccountsEndpoint.Sorted(await store.GetBankAccountsAsync(uid))));
        });
        return api;
    }
}
