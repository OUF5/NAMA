using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Api.Domain;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.FiscalYears;

// PRD 7.5 GET /api/fiscal-years and POST /api/fiscal-years/next (PRD 5.8). The months are the
// ones stored on each FY (R-CAL-09).
public static class FiscalYearsEndpoint
{
    public record FiscalYearDto(
        string Id,
        string Label,
        DateOnly StartDate,
        DateOnly EndDate,
        bool IsTransition,
        IReadOnlyList<MeEndpoint.PeriodDto> Periods)
    {
        public static FiscalYearDto From(FiscalYear fiscalYear) => new(
            fiscalYear.Id, fiscalYear.Label, fiscalYear.StartDate, fiscalYear.EndDate, fiscalYear.IsTransition,
            fiscalYear.Periods.Select(MeEndpoint.PeriodDto.From).ToList());
    }

    // FY-01 banner: the latest FY, and the label and first day of the FY that Start creates.
    public record RolloverDto(string FyId, string Label, DateOnly EndDate, string NextLabel, DateOnly NextStartDate);

    // The new /api/me is included, so the web app needs no second call (top bar, current FY, FY-03 dates).
    public record StartNextResponse(FiscalYearDto FiscalYear, int CopiedPlans, MeEndpoint.MeResponse Me);

    public static IEndpointRouteBuilder MapFiscalYears(this IEndpointRouteBuilder api)
    {
        api.MapGet("/fiscal-years", async (ClaimsPrincipal user, IUserStore store) =>
            (await store.GetFiscalYearsAsync(user.FindFirstValue("sub")!))
                .OrderBy(fiscalYear => fiscalYear.StartDate)
                .Select(FiscalYearDto.From)
                .ToList());

        // FY-02, R-CAL-07, R-CAL-08: one FY per call, from the profile's current settings. 409 before
        // the last month of the latest FY, and when that FY was just started (a second click or a race).
        api.MapPost("/fiscal-years/next", async (StartNextRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time,
            EmailAllowlist admins) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var profile = await store.GetProfileAsync(uid);
            var fiscalYears = await store.GetFiscalYearsAsync(uid);
            if (profile is null || FiscalCalendar.Latest(fiscalYears) is not { } latest)
            {
                return Results.NotFound();
            }
            if (!FiscalCalendar.CanStartNext(latest, FiscalCalendar.Today(time, profile.Timezone)))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    detail: $"{latest.Label} ends on {Format(latest.EndDate)}. The next fiscal year can be started from {Format(latest.Periods[^1].StartDate)}.");
            }

            var next = FiscalCalendar.Named(
                FiscalCalendar.NextFiscalYear(latest, profile.FiscalStartMonth, profile.PaydayDay), profile.MonthNaming);
            var plans = request.CopyPlans == true ? await CopyPlansAsync(uid, fiscalYears, next, store) : [];
            if (!await store.StartFiscalYearAsync(uid, next, plans))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: $"{next.Label} was already started.");
            }
            return Results.Created($"/api/fiscal-years/{next.Id}",
                new StartNextResponse(FiscalYearDto.From(next), plans.Count, await MeEndpoint.LoadAsync(uid, admins.IsAdmin(user), store, time)));
        });

        return api;
    }

    // FY-01: set while the next FY can be started (the last month of the latest FY, and after it ends).
    public static RolloverDto? Rollover(IReadOnlyList<FiscalYear> fiscalYears, UserProfile profile, DateOnly today)
    {
        if (FiscalCalendar.Latest(fiscalYears) is not { } latest || !FiscalCalendar.CanStartNext(latest, today))
        {
            return null;
        }
        var next = FiscalCalendar.NextFiscalYear(latest, profile.FiscalStartMonth, profile.PaydayDay);
        return new RolloverDto(latest.Id, latest.Label, latest.EndDate, next.Label, next.StartDate);
    }

    // FY-02 (owner's choice, M8): the same month a year earlier, from whichever stored FY holds it.
    // Archived sub-categories, and the ones of archived categories, are not copied.
    private static async Task<IReadOnlyList<PlanAmounts>> CopyPlansAsync(
        string uid, IReadOnlyList<FiscalYear> fiscalYears, FiscalYear next, IUserStore store)
    {
        var sourceKeys = next.Periods.Select(period => FiscalCalendar.YearEarlierKey(period.Key)).ToHashSet(StringComparer.Ordinal);
        var sources = new List<PlanAmounts>();
        foreach (var fiscalYear in fiscalYears.Where(fiscalYear => fiscalYear.Periods.Any(period => sourceKeys.Contains(period.Key))))
        {
            sources.AddRange(await store.GetPlansAsync(uid, fiscalYear.Id));
        }
        var archivedCategories = (await store.GetCategoriesAsync(uid))
            .Where(category => category.Archived)
            .Select(category => category.Id)
            .ToHashSet();
        var active = (await store.GetSubCategoriesAsync(uid))
            .Where(sub => !sub.Archived && !archivedCategories.Contains(sub.CategoryId))
            .Select(sub => sub.Id)
            .ToHashSet();
        return PlanCalculator.CopyForNextFiscalYear(sources, next.Periods, active);
    }

    // "27 Dec 2026"
    private static string Format(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}

// FY-02: the checkbox "Copy planned amounts" (on by default in the web app).
public sealed class StartNextRequest
{
    [Required]
    public bool? CopyPlans { get; init; }
}
