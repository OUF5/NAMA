using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.BankAccounts;
using Api.Features.Categories;
using Api.Features.FiscalYears;
using Api.Features.Income;
using Api.Features.People;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Settings;

// PRD 5.7 Settings. Owner's choice (M8, NFR-01): GET /api/settings brings every section in one
// call (an addition to PRD 7.5); the version comes from GET /api/health. People (STG-07) is there
// for admins only, and null for everyone else.
public static class SettingsEndpoint
{
    // STG-02, R-CAL-07: the settings apply from the FY after the latest stored one. NextFiscalYear is
    // that FY as it would be created now (not stored, so its months are only a preview).
    public record CalendarDto(int FiscalStartMonth, int PaydayDay, DateOnly TakesEffectFrom, FiscalYearsEndpoint.FiscalYearDto NextFiscalYear);

    public record SettingsResponse(
        MeEndpoint.ProfileDto Profile,
        CalendarDto Calendar,
        IncomeEndpoint.IncomeResponse Income,
        IReadOnlyList<BankAccountsEndpoint.BankAccountDto> BankAccounts,
        IReadOnlyList<CategoriesEndpoint.CategoryDto> Categories,
        PeopleEndpoint.PeopleDto? People);

    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder api)
    {
        api.MapGet("/settings", async (ClaimsPrincipal user, IUserStore store, TimeProvider time, IAccessStore access, EmailAllowlist admins) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var profile = await store.GetProfileAsync(uid);
            var fiscalYears = await store.GetFiscalYearsAsync(uid);
            if (profile is null || Calendar(profile, fiscalYears) is not { } calendar
                || await IncomeEndpoint.LoadAsync(uid, null, store, time) is not { } income)
            {
                return Results.NotFound();
            }
            var subCategories = await store.GetSubCategoriesAsync(uid);
            return Results.Ok(new SettingsResponse(
                MeEndpoint.ProfileDto.From(profile),
                calendar,
                income,
                BankAccountsEndpoint.Sorted(await store.GetBankAccountsAsync(uid)),
                (await store.GetCategoriesAsync(uid))
                    .OrderBy(category => category.SortOrder)
                    .Select(category => CategoriesEndpoint.ToDto(category, subCategories))
                    .ToList(),
                admins.IsAdmin(user) ? await PeopleEndpoint.LoadAsync(access, admins, time, profile.Timezone) : null));
        });

        // STG-02, R-CAL-07: saved right away, applied when the next FY is started. Stored FYs keep
        // their dates (R-CAL-09).
        api.MapPut("/settings/calendar", async (CalendarRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var profile = await store.GetProfileAsync(uid);
            if (profile is null)
            {
                return Results.NotFound();
            }
            await store.UpdateCalendarAsync(uid, request.FiscalStartMonth, request.PaydayDay);
            var calendar = Calendar(profile with { FiscalStartMonth = request.FiscalStartMonth, PaydayDay = request.PaydayDay },
                await store.GetFiscalYearsAsync(uid));
            return calendar is null ? Results.NotFound() : Results.Ok(calendar);
        });

        return api;
    }

    // R-CAL-08: the next FY starts the day after the latest FY ends.
    private static CalendarDto? Calendar(UserProfile profile, IReadOnlyList<FiscalYear> fiscalYears)
    {
        if (FiscalCalendar.Latest(fiscalYears) is not { } latest)
        {
            return null;
        }
        var next = FiscalCalendar.NextFiscalYear(latest, profile.FiscalStartMonth, profile.PaydayDay);
        return new CalendarDto(profile.FiscalStartMonth, profile.PaydayDay, next.StartDate, FiscalYearsEndpoint.FiscalYearDto.From(next));
    }
}

// PRD 4.7: payday 1 to 31, FY start month 1 to 12.
public sealed class CalendarRequest
{
    [Range(1, 12)]
    public int FiscalStartMonth { get; init; }

    [Range(1, 31)]
    public int PaydayDay { get; init; }
}
