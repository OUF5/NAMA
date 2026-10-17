using System.Security.Claims;
using Api.Domain;
using Api.Features.Entries;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Indicator;

// PRD 7.5 GET /api/indicator (ENT-01): planned, spent so far, remaining and percent for one
// sub-category in the month of a date.
public static class IndicatorEndpoint
{
    // Percent is null when nothing is planned ("No plan for this month").
    public record IndicatorResponse(MeEndpoint.PeriodDto Period, decimal Planned, decimal Spent, decimal Remaining, int? Percent);

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
            var planned = plan is null ? 0 : PlanCalculator.AmountFor(plan, found.Period.Key);
            var spent = (await store.GetEntriesAsync(uid, subCategoryId, found.Period.Key)).Sum(entry => entry.AmountMinor);

            return Results.Ok(new IndicatorResponse(
                MeEndpoint.PeriodDto.From(found.Period),
                Money.FromMinor(planned),
                Money.FromMinor(spent),
                Money.FromMinor(PlanCalculator.Remaining(planned, spent)),
                PlanCalculator.Percent(planned, spent)));
        });
        return api;
    }
}
