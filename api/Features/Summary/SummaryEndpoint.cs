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
    // Balance is null when the account has no opening balance yet (R-BAL-01).
    public record AccountDto(string Id, string Name, decimal? Balance);

    // The month in progress, for the whole budget. Day is today's day of the month (1 on payday), Days
    // the month's length, and DaysLeft counts today (DSH-01's marker). Remaining carries (R-PLN-05)
    // and Unassigned carries (R-PLN-03), from the FY's first month.
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
        decimal Unassigned);

    // A month yet to come with its income minus its planned (R-PLN-07).
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
        decimal ProjectedSaving);

    // Balance is the active accounts' total, null until every active account has an opening balance
    // and income has a split (Complete). Accounts are the active ones, by name. Month and Projection
    // are null when no FY contains today.
    public record SummaryResponse(
        decimal? Balance, bool Complete, IReadOnlyList<AccountDto> Accounts, MonthDto? Month, ProjectionDto? Projection);

    public static IEndpointRouteBuilder MapSummary(this IEndpointRouteBuilder api)
    {
        api.MapGet("/summary", async (ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var profile = await store.GetProfileAsync(uid);
            if (profile is null)
            {
                return Results.NotFound();
            }
            var current = FiscalCalendar.FindPeriod(await store.GetFiscalYearsAsync(uid), FiscalCalendar.Today(time, profile.Timezone));
            var data = (await BanksEndpoint.LoadAsync(uid, store, time, current?.FiscalYear.StartDate))!;

            var balance = data.Complete && data.Total is { } total ? total : (long?)null;
            MonthDto? month = null;
            ProjectionDto? projection = null;
            if (current is { } found)
            {
                var periods = found.FiscalYear.Periods.TakeWhile(period => string.CompareOrdinal(period.Key, found.Period.Key) <= 0).ToList();
                // R-PLN-06: carried from the month NAMA was set up, or the FY's first month.
                var carryStart = PlanCalculator.CarryStart(found.FiscalYear.Periods, profile.StartedOn);
                var plans = await store.GetPlansAsync(uid, found.FiscalYear.Id);
                var entries = data.Entries.Where(entry => entry.FyId == found.FiscalYear.Id).ToList();
                var toToday = PlanCalculator.Carry(periods.Select(period => (
                    PlanCalculator.MonthPlanned(plans, period.Key),
                    entries.Where(entry => entry.PeriodKey == period.Key).Sum(entry => entry.AmountMinor))), carryStart);
                var remaining = toToday[^1];
                var wholeYear = PlanCalculator.RunningUnassigned(found.FiscalYear.Periods, data.Income, plans, carryStart);
                var unassigned = wholeYear[periods.Count - 1];
                month = new MonthDto(
                    MeEndpoint.PeriodDto.From(found.Period),
                    data.Today.DayNumber - found.Period.StartDate.DayNumber + 1,
                    found.Period.EndDate.DayNumber - found.Period.StartDate.DayNumber + 1,
                    found.Period.EndDate.DayNumber - data.Today.DayNumber + 1,
                    Money.FromMinor(unassigned.Income),
                    Money.FromMinor(remaining.CarriedIn),
                    Money.FromMinor(remaining.Planned),
                    Money.FromMinor(remaining.Spent),
                    Money.FromMinor(remaining.Remaining),
                    Money.FromMinor(unassigned.Unassigned));

                var projected = PlanCalculator.Project(wholeYear, toToday, carryStart, balance);
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
                    Money.FromMinor(projected.ProjectedSaving));
            }

            return Results.Ok(new SummaryResponse(
                balance is { } known ? Money.FromMinor(known) : null,
                data.Complete,
                data.Accounts
                    .Where(account => !account.Archived)
                    .OrderBy(account => Names.Lower(account.Name), StringComparer.Ordinal)
                    .Select(account => new AccountDto(
                        account.Id,
                        account.Name,
                        data.Balances.TryGetValue(account.Id, out var balance) ? Money.FromMinor(balance.Balance) : null))
                    .ToList(),
                month,
                projection));
        });
        return api;
    }
}
