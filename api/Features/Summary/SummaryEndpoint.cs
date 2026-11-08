using System.Security.Claims;
using Api.Domain;
using Api.Features.Banks;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Summary;

// UX-08 top bar: the current month's numbers and the bank balances, on every page. The web app loads
// it once and again after every change (NFR-01: it is the shell's, not a page's). One entries query
// serves both the balances and the month (BanksEndpoint.LoadAsync).
public static class SummaryEndpoint
{
    // Balance and OpeningDate are null when the account has no opening balance yet (R-BAL-01). The
    // income preview (INC-01) counts the paydays from OpeningDate on, as R-BAL-03 does.
    public record AccountDto(string Id, string Name, decimal? Balance, DateOnly? OpeningDate);

    // The month in progress, for the whole budget. Day is today's day of the month (1 on payday), Days
    // the month's length, and DaysLeft counts today (DSH-01's marker). Income is the actual income
    // (R-INC-01). Remaining carries (R-PLN-05) and Unassigned carries (R-PLN-03, planned income), from
    // the FY's first month. OverspentLastMonth is what went over plan last month and is not made up yet
    // (R-PLN-09, each sub-category on its own, added up; 0 in the FY's first month), for Dashboard's
    // current month (owner note 32).
    public record MonthDto(
        MeEndpoint.PeriodDto Period,
        int Day,
        int Days,
        int DaysLeft,
        decimal Income,
        decimal CarriedIn,
        decimal Planned,
        decimal Spent,
        decimal Remaining,
        decimal Unassigned,
        decimal OverspentLastMonth);

    // A month yet to come with its planned income minus its planned (R-PLN-07).
    public record FutureMonthDto(MeEndpoint.PeriodDto Period, decimal Unallocated);

    // Dashboard's Projection and Planned saving vs projection (DSH-05, DSH-06, R-PLN-07, R-PLN-08) for
    // the FY that contains today. BalanceToday and ProjectedBalance are null until the balance is
    // complete. It is here, not in the overview, because the balances are (no extra reads, NFR-01).
    public record ProjectionDto(
        string FiscalYearLabel,
        DateOnly EndDate,
        decimal? BalanceToday,
        decimal StillToSpend,
        decimal FutureUnallocated,
        IReadOnlyList<FutureMonthDto> FutureMonths,
        decimal? ProjectedBalance,
        decimal SavedToDate,
        decimal PlannedSaved,
        decimal ProjectedSaving,
        // R-PLN-06: the first month SavedToDate and PlannedSaved count from when it is not the FY's
        // first month (NAMA was set up later), so Dashboard can say so (owner note 11). Null otherwise.
        MeEndpoint.PeriodDto? CountedFrom);

    public record LastExpenseDto(DateOnly Date, string CategoryId, string SubCategoryId, string BankAccountId, decimal Amount, string Note)
    {
        public static LastExpenseDto From(Entry entry) => new(
            entry.Date, entry.CategoryId, entry.SubCategoryId, entry.BankAccountId, Money.FromMinor(entry.AmountMinor), entry.Note);
    }

    public record LastTransferDto(string Id, DateOnly Date, string FromBankAccountId, string ToBankAccountId, decimal Amount, string Note)
    {
        public static LastTransferDto From(Transfer transfer) => new(
            transfer.Id, transfer.Date, transfer.FromBankAccountId, transfer.ToBankAccountId, Money.FromMinor(transfer.AmountMinor), transfer.Note);
    }

    public record LastIncomeDto(string Label, decimal Amount, string FromPeriodKey, string? ToPeriodKey, string? BankAccountId)
    {
        public static LastIncomeDto From(IncomeItem item) => new(
            item.Label, Money.FromMinor(item.AmountMinor), item.FromPeriodKey, item.ToPeriodKey, item.BankAccountId);
    }

    // ENT-05 (owner note 35): for Add's panels, the one added last, by when it was added, not by its
    // date, so a back-dated expense counts. Each map is keyed by an id; an id with nothing added is
    // left out. Income is the last additional item (the salary is not one). It is here, not in its own
    // call, because the balances read all of it already (NFR-01); expenses go back as far as they do.
    public record LastAddedDto(
        IReadOnlyDictionary<string, LastExpenseDto> ExpenseBySubCategory,
        IReadOnlyDictionary<string, LastExpenseDto> ExpenseByAccount,
        IReadOnlyDictionary<string, LastTransferDto> TransferFrom,
        IReadOnlyDictionary<string, LastTransferDto> TransferTo,
        LastIncomeDto? Income)
    {
        public static LastAddedDto From(BalanceData data) => new(
            Latest(data.Entries, entry => entry.SubCategoryId, entry => entry.CreatedAt, LastExpenseDto.From),
            Latest(data.Entries, entry => entry.BankAccountId, entry => entry.CreatedAt, LastExpenseDto.From),
            Latest(data.Transfers, transfer => transfer.FromBankAccountId, transfer => transfer.CreatedAt, LastTransferDto.From),
            Latest(data.Transfers, transfer => transfer.ToBankAccountId, transfer => transfer.CreatedAt, LastTransferDto.From),
            data.IncomeItems.Where(item => item.Kind == IncomeKind.Additional).MaxBy(item => item.CreatedAt) is { } income
                ? LastIncomeDto.From(income)
                : null);

        private static Dictionary<string, TDto> Latest<T, TDto>(
            IEnumerable<T> items, Func<T, string> key, Func<T, DateTimeOffset> createdAt, Func<T, TDto> toDto) =>
            items.GroupBy(key).ToDictionary(group => group.Key, group => toDto(group.MaxBy(createdAt)!));
    }

    // Balance is the active accounts' total, null until every active account has an opening balance
    // and income has a split (Complete). Accounts are the active ones, by name. Month and Projection
    // are null when no FY contains today.
    public record SummaryResponse(
        decimal? Balance, bool Complete, IReadOnlyList<AccountDto> Accounts, MonthDto? Month, ProjectionDto? Projection, LastAddedDto LastAdded);

    // The month in progress for the whole budget: its carried remaining (R-PLN-05), its Unassigned
    // (R-PLN-03) and the projection (R-PLN-07, R-PLN-08), all from the month NAMA was set up (R-PLN-06).
    // The FY's entries come from data (one entries query serves the balances too). Tracker's Year view
    // uses it for the projected month ends (owner note 9).
    public static async Task<(CarriedMonth Remaining, UnassignedMonth Unassigned, Projection Projection, long OverspentLastMonth)> CurrentMonthAsync(
        string uid, IUserStore store, BalanceData data, (FiscalYear FiscalYear, Period Period) found, long? balance)
    {
        var periods = found.FiscalYear.Periods.TakeWhile(period => string.CompareOrdinal(period.Key, found.Period.Key) <= 0).ToList();
        var carryStart = PlanCalculator.CarryStart(found.FiscalYear.Periods, data.Profile.StartedOn);
        var plans = await store.GetPlansAsync(uid, found.FiscalYear.Id);
        var entries = data.Entries.Where(entry => entry.FyId == found.FiscalYear.Id).ToList();
        var toToday = PlanCalculator.Carry(periods.Select(period => (
            PlanCalculator.MonthPlanned(plans, period.Key),
            entries.Where(entry => entry.PeriodKey == period.Key).Sum(entry => entry.AmountMinor))), carryStart);
        var wholeYear = PlanCalculator.RunningUnassigned(found.FiscalYear.Periods, data.Income.Planned, plans, carryStart);

        // R-PLN-09 (owner note 32): last month, within the FY, whatever month NAMA was set up in.
        var overspent = 0L;
        if (periods.Count > 1)
        {
            var last = periods[^2].Key;
            var spent = entries.Where(entry => entry.PeriodKey == last)
                .GroupBy(entry => entry.SubCategoryId)
                .ToDictionary(group => group.Key, group => group.Sum(entry => entry.AmountMinor));
            var planned = plans.ToDictionary(plan => plan.SubCategoryId, plan => PlanCalculator.AmountFor(plan, last));
            overspent = planned.Keys.Union(spent.Keys)
                .Sum(id => PlanCalculator.Overspent([(planned.GetValueOrDefault(id), spent.GetValueOrDefault(id))]));
        }
        return (toToday[^1], wholeYear[periods.Count - 1], PlanCalculator.Project(wholeYear, toToday, carryStart, balance, data.Income.Actual), overspent);
    }

    public static IEndpointRouteBuilder MapSummary(this IEndpointRouteBuilder api)
    {
        api.MapGet("/summary", async (ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
            await LoadAsync(user.FindFirstValue("sub")!, store, time) is { } summary ? Results.Ok(summary) : Results.NotFound());
        return api;
    }

    // Null when the user has no profile. Also the Claude connector's get_summary (MCP-04).
    public static async Task<SummaryResponse?> LoadAsync(string uid, IUserStore store, TimeProvider time)
    {
        var profile = await store.GetProfileAsync(uid);
        if (profile is null)
        {
            return null;
        }
        var current = FiscalCalendar.FindPeriod(await store.GetFiscalYearsAsync(uid), FiscalCalendar.Today(time, profile.Timezone));
        var data = (await BanksEndpoint.LoadAsync(uid, store, time, current?.FiscalYear.StartDate))!;

        var balance = data.Complete && data.Total is { } total ? total : (long?)null;
        MonthDto? month = null;
        ProjectionDto? projection = null;
        if (current is { } found)
        {
            var (remaining, unassigned, projected, overspent) = await CurrentMonthAsync(uid, store, data, found, balance);
            month = new MonthDto(
                MeEndpoint.PeriodDto.From(found.Period),
                data.Today.DayNumber - found.Period.StartDate.DayNumber + 1,
                found.Period.EndDate.DayNumber - found.Period.StartDate.DayNumber + 1,
                found.Period.EndDate.DayNumber - data.Today.DayNumber + 1,
                Money.FromMinor(data.Income.Actual.GetValueOrDefault(found.Period.Key)),
                Money.FromMinor(remaining.CarriedIn),
                Money.FromMinor(remaining.Planned),
                Money.FromMinor(remaining.Spent),
                Money.FromMinor(remaining.Remaining),
                Money.FromMinor(unassigned.Unassigned),
                Money.FromMinor(overspent));

            projection = new ProjectionDto(
                found.FiscalYear.Label,
                found.FiscalYear.EndDate,
                projected.BalanceToday is { } today ? Money.FromMinor(today) : null,
                Money.FromMinor(projected.StillToSpend),
                Money.FromMinor(projected.FutureUnallocated),
                projected.FutureMonths
                    .Select(future => new FutureMonthDto(
                        MeEndpoint.PeriodDto.From(found.FiscalYear.Periods.Single(period => period.Key == future.PeriodKey)),
                        Money.FromMinor(future.ThisMonth)))
                    .ToList(),
                projected.ProjectedBalance is { } end ? Money.FromMinor(end) : null,
                Money.FromMinor(projected.SavedToDate),
                Money.FromMinor(projected.PlannedSaved),
                Money.FromMinor(projected.ProjectedSaving),
                PlanCalculator.CarryStart(found.FiscalYear.Periods, data.Profile.StartedOn) is var start and > 0
                    ? MeEndpoint.PeriodDto.From(found.FiscalYear.Periods[Math.Min(start, found.FiscalYear.Periods.Count - 1)])
                    : null);
        }

        return new SummaryResponse(
            balance is { } known ? Money.FromMinor(known) : null,
            data.Complete,
            data.Accounts
                .Where(account => !account.Archived)
                .OrderBy(account => Names.Lower(account.Name), StringComparer.Ordinal)
                .Select(account => new AccountDto(
                    account.Id,
                    account.Name,
                    data.Balances.TryGetValue(account.Id, out var balance) ? Money.FromMinor(balance.Balance) : null,
                    account.Opening?.Date))
                .ToList(),
            month,
            projection,
            LastAddedDto.From(data));
    }
}
