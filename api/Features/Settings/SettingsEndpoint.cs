using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.Connect;
using Api.Features.FiscalYears;
using Api.Features.Income;
using Api.Features.People;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Settings;

// PRD 5.7 Settings. Owner's choice (M8, NFR-01): GET /api/settings brings every section in one
// call (an addition to PRD 7.5); the version comes from GET /api/health. People (STG-07) is there
// for admins only, and null for everyone else. Bank accounts live on Banks (BNK-02, 20 Oct 2026), and
// categories on Plan's Categories tab, which reads GET /api/categories (owner note 9).
public static class SettingsEndpoint
{
    // STG-02, R-CAL-07: the start month and payday apply from the FY after the latest stored one.
    // NextFiscalYear is that FY as it would be created now (not stored, so its months are only a
    // preview). MonthNaming applies right away (R-CAL-11); MonthKeyOffset is /api/me's.
    public record CalendarDto(int FiscalStartMonth, int PaydayDay, string MonthNaming, int MonthKeyOffset, DateOnly TakesEffectFrom,
        FiscalYearsEndpoint.FiscalYearDto NextFiscalYear);

    public record SettingsResponse(
        MeEndpoint.ProfileDto Profile,
        CalendarDto Calendar,
        IncomeEndpoint.IncomeResponse Income,
        PeopleEndpoint.PeopleDto? People,
        // STG-08, MCP-05: null while the Claude connector is off (MCP-06).
        IReadOnlyList<ConnectionsEndpoint.ConnectionDto>? Connections);

    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder api)
    {
        api.MapGet("/settings", async (ClaimsPrincipal user, IUserStore store, TimeProvider time, IAccessStore access, EmailAllowlist admins,
            IConnectionStore connections, IConfiguration config) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var profile = await store.GetProfileAsync(uid);
            var fiscalYears = await store.GetFiscalYearsAsync(uid);
            if (profile is null || Calendar(profile, fiscalYears) is not { } calendar
                || await IncomeEndpoint.LoadAsync(uid, null, store, time) is not { } income)
            {
                return Results.NotFound();
            }
            return Results.Ok(new SettingsResponse(
                MeEndpoint.ProfileDto.From(profile),
                calendar,
                income,
                admins.IsAdmin(user) ? await PeopleEndpoint.LoadAsync(access, admins, time, profile.Timezone) : null,
                McpAuth.Origin(config) is null ? null : await ConnectionsEndpoint.LoadAsync(uid, connections, time, profile.Timezone)));
        });

        // STG-02, R-CAL-07: saved right away, applied when the next FY is started. Stored FYs keep
        // their dates (R-CAL-09). R-CAL-11: the month naming renames every stored month at once; no
        // naming keeps the current one.
        api.MapPut("/settings/calendar", async (CalendarRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var profile = await store.GetProfileAsync(uid);
            if (profile is null)
            {
                return Results.NotFound();
            }
            var naming = request.MonthNaming is null ? profile.MonthNaming : FirestoreDocuments.ToMonthNaming(request.MonthNaming);
            await store.UpdateCalendarAsync(uid, request.FiscalStartMonth, request.PaydayDay, naming);
            var calendar = Calendar(
                profile with { FiscalStartMonth = request.FiscalStartMonth, PaydayDay = request.PaydayDay, MonthNaming = naming },
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
        var next = FiscalCalendar.Named(
            FiscalCalendar.NextFiscalYear(latest, profile.FiscalStartMonth, profile.PaydayDay), profile.MonthNaming);
        return new CalendarDto(profile.FiscalStartMonth, profile.PaydayDay, FirestoreDocuments.ToMonthNamingName(profile.MonthNaming),
            MeEndpoint.MonthKeyOffset(profile, fiscalYears), next.StartDate, FiscalYearsEndpoint.FiscalYearDto.From(next));
    }
}

// PRD 4.7: payday 1 to 31, FY start month 1 to 12. R-CAL-11: "end" or "payday".
public sealed class CalendarRequest
{
    [Range(1, 12)]
    public int FiscalStartMonth { get; init; }

    [Range(1, 31)]
    public int PaydayDay { get; init; }

    [AllowedValues(null, "end", "payday")]
    public string? MonthNaming { get; init; }
}
