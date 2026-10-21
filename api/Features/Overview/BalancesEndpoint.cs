using System.Security.Claims;
using Api.Domain;
using Api.Features.Banks;
using Api.Features.Summary;
using Api.Infrastructure;

namespace Api.Features.Overview;

// Owner note 9: GET /api/fiscal-years/{fyId}/balances, the Balance row of Tracker's Year view. Only the
// Year view makes this call (with the overview, NFR-01), because balances read every entry since the
// earliest opening date. Each month's balance is the active accounts' total at its end:
// - a closed month: the real balance on its last day (R-BAL-03), unknown before an opening date;
// - the month in progress and the months yet to come: projected (R-PLN-07), each month's end being the
//   one before plus its income minus its planned; the FY's last month is the projected balance.
// Months of an FY that has not started are unknown. Everything is null until the balance is complete.
public static class BalancesEndpoint
{
    public record MonthBalanceDto(string PeriodKey, decimal? Balance, bool Projected);

    public record BalancesResponse(bool Complete, IReadOnlyList<MonthBalanceDto> Months);

    public static IEndpointRouteBuilder MapBalances(this IEndpointRouteBuilder api)
    {
        api.MapGet("/fiscal-years/{fyId}/balances", async (string fyId, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var fiscalYear = (await store.GetFiscalYearsAsync(uid)).FirstOrDefault(item => item.Id == fyId);
            if (fiscalYear is null || await BanksEndpoint.LoadAsync(uid, store, time, fiscalYear.StartDate) is not { } data)
            {
                return Results.NotFound();
            }
            var balance = data.Complete && data.Total is { } total ? total : (long?)null;
            var current = FiscalCalendar.FindPeriod(data.FiscalYears, data.Today);
            var projected = current is { } found && found.FiscalYear.Id == fyId
                ? (await SummaryEndpoint.CurrentMonthAsync(uid, store, data, found, balance)).Projection.MonthEnds()
                    .ToDictionary(end => end.PeriodKey, end => end.Balance)
                : [];

            return Results.Ok(new BalancesResponse(
                data.Complete,
                fiscalYear.Periods.Select(period => period.EndDate < data.Today
                        ? new MonthBalanceDto(period.Key, data.TotalOn(period.EndDate) is { } end ? Money.FromMinor(end) : null, false)
                        : new MonthBalanceDto(
                            period.Key,
                            projected.GetValueOrDefault(period.Key) is { } ahead ? Money.FromMinor(ahead) : null,
                            true))
                    .ToList()));
        });
        return api;
    }
}
