using System.Security.Claims;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Features.Profile;

// PRD 7.5 GET /api/me, called right after sign-in: profile, setupCompleted, current FY and month.
// The current month is also shown in the top bar (UX-02).
public static class MeEndpoint
{
    public record ProfileDto(
        string DisplayName,
        string Email,
        string ProjectName,
        string Currency,
        string Timezone,
        int FiscalStartMonth,
        int PaydayDay,
        string? LastBankAccountId);

    // DateOnly is sent as YYYY-MM-DD.
    public record PeriodDto(string Key, string Label, DateOnly StartDate, DateOnly EndDate)
    {
        public static PeriodDto From(Period period) => new(period.Key, period.Label, period.StartDate, period.EndDate);
    }

    // Today (R-CAL-10) and the first and last day of the stored FYs (FY-03, R-STA-03) are for the
    // Add form, so it needs no extra call (M5, NFR-01).
    public record MeResponse(
        bool SetupCompleted,
        ProfileDto? Profile,
        string? CurrentFyId,
        string? CurrentPeriodKey,
        PeriodDto? CurrentPeriod,
        DateOnly? Today,
        DateOnly? FirstDate,
        DateOnly? LastDate);

    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder api)
    {
        api.MapGet("/me", (ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
            LoadAsync(user.FindFirstValue("sub")!, store, time));
        return api;
    }

    // R-CAL-10: "today" in the profile time zone. The current FY and month are null when no
    // stored FY contains today (after the last FY ends, before the next one is started).
    public static async Task<MeResponse> LoadAsync(string uid, IUserStore store, TimeProvider time)
    {
        var profile = await store.GetProfileAsync(uid);
        if (profile is null)
        {
            return new MeResponse(SetupCompleted: false, Profile: null, CurrentFyId: null, CurrentPeriodKey: null,
                CurrentPeriod: null, Today: null, FirstDate: null, LastDate: null);
        }
        var today = FiscalCalendar.Today(time, profile.Timezone);
        var fiscalYears = await store.GetFiscalYearsAsync(uid);
        var current = FiscalCalendar.FindPeriod(fiscalYears, today);
        return new MeResponse(
            profile.SetupCompleted,
            new ProfileDto(profile.DisplayName, profile.Email, profile.ProjectName, profile.Currency,
                profile.Timezone, profile.FiscalStartMonth, profile.PaydayDay, profile.LastBankAccountId),
            current?.FiscalYear.Id,
            current?.Period.Key,
            current is { } found ? PeriodDto.From(found.Period) : null,
            today,
            fiscalYears.Count == 0 ? null : fiscalYears.Min(fiscalYear => fiscalYear.StartDate),
            fiscalYears.Count == 0 ? null : fiscalYears.Max(fiscalYear => fiscalYear.EndDate));
    }
}
