using System.Security.Claims;
using Api.Domain;
using Api.Features.Entries;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Indicator;

// PRD 7.5 GET /api/indicator (ENT-01): planned, spent so far, remaining and percent for one
// sub-category in the month of a date. R-PLN-05: what was left in the FY's earlier months carries in,
// so available = carried in + planned, and remaining = available - spent.
public static class IndicatorEndpoint
{
    // Percent is null when nothing is available ("No plan for this month").
    public record IndicatorResponse(
        MeEndpoint.PeriodDto Period, decimal CarriedIn, decimal Planned, decimal Available, decimal Spent, decimal Remaining, int? Percent);

    public static IEndpointRouteBuilder MapIndicator(this IEndpointRouteBuilder api)
    {
        api.MapGet("/indicator", async (string subCategoryId, DateOnly date, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (!(await store.GetSubCategoriesAsync(uid)).Any(sub => sub.Id == subCategoryId))
            {
                return Results.NotFound();
            }

            // R-CAL-09: the month is one of the stored months, found from the date (R-STA-03).
            if (FiscalCalendar.FindPeriod(await store.GetFiscalYearsAsync(uid), date) is not { } found)
            {
                return EntriesEndpoint.OutsideFiscalYears();
            }

            var plan = (await store.GetPlansAsync(uid, found.FiscalYear.Id)).FirstOrDefault(p => p.SubCategoryId == subCategoryId);
            var spent = (await store.GetSubCategoryEntriesAsync(uid, subCategoryId, found.FiscalYear.Id))
                .GroupBy(entry => entry.PeriodKey)
                .ToDictionary(group => group.Key, group => group.Sum(entry => entry.AmountMinor));
            // The FY's months up to the month of the date, carried from the month NAMA was set up (R-PLN-06).
            var carryStart = PlanCalculator.CarryStart(found.FiscalYear.Periods, (await store.GetProfileAsync(uid))?.StartedOn);
            var months = PlanCalculator.Carry(found.FiscalYear.Periods
                .TakeWhile(period => string.CompareOrdinal(period.Key, found.Period.Key) <= 0)
                .Select(period => (plan is null ? 0 : PlanCalculator.AmountFor(plan, period.Key), spent.GetValueOrDefault(period.Key))),
                carryStart);
            var month = months[^1];

            return Results.Ok(new IndicatorResponse(
                MeEndpoint.PeriodDto.From(found.Period),
                Money.FromMinor(month.CarriedIn),
                Money.FromMinor(month.Planned),
                Money.FromMinor(month.Available),
                Money.FromMinor(month.Spent),
                Money.FromMinor(month.Remaining),
                PlanCalculator.Percent(month.Available, month.Spent)));
        });
        return api;
    }
}
