using System.Security.Claims;
using Api.Domain;
using Api.Features.Entries;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Indicator;

// PRD 7.5 GET /api/indicator (ENT-01): planned, spent so far, remaining and percent for one
// sub-category in the month of a date, the month on its own (owner note 32, as Tracker's month):
// remaining = planned - spent. Beside it, what went over plan last month and is not made up yet
// (R-PLN-09, over the last month only; none in the FY's first month).
public static class IndicatorEndpoint
{
    // Percent is null when nothing is planned ("No plan for this month").
    public record IndicatorResponse(
        MeEndpoint.PeriodDto Period, decimal Planned, decimal Spent, decimal Remaining, int? Percent, decimal OverspentLastMonth);

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
            (long Planned, long Spent) Month(Period period) =>
                (plan is null ? 0 : PlanCalculator.AmountFor(plan, period.Key), spent.GetValueOrDefault(period.Key));
            var (planned, spentNow) = Month(found.Period);
            // R-PLN-09: the month before, within the FY, whatever month NAMA was set up in.
            var index = found.FiscalYear.Periods.ToList().FindIndex(period => period.Key == found.Period.Key);
            var overspent = index > 0 ? PlanCalculator.Overspent([Month(found.FiscalYear.Periods[index - 1])]) : 0;

            return Results.Ok(new IndicatorResponse(
                MeEndpoint.PeriodDto.From(found.Period),
                Money.FromMinor(planned),
                Money.FromMinor(spentNow),
                Money.FromMinor(PlanCalculator.Remaining(planned, spentNow)),
                PlanCalculator.Percent(planned, spentNow),
                Money.FromMinor(overspent)));
        });
        return api;
    }
}
