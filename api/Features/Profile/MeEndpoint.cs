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
        string MonthNaming,
        string? LastBankAccountId)
    {
        public static ProfileDto From(UserProfile profile) => new(profile.DisplayName, profile.Email, profile.ProjectName,
            profile.Currency, profile.Timezone, profile.FiscalStartMonth, profile.PaydayDay,
            FirestoreDocuments.ToMonthNamingName(profile.MonthNaming), profile.LastBankAccountId);
    }

    // DateOnly is sent as YYYY-MM-DD.
    public record PeriodDto(string Key, string Label, DateOnly StartDate, DateOnly EndDate)
    {
        public static PeriodDto From(Period period) => new(period.Key, period.Label, period.StartDate, period.EndDate);
    }

    // Today (R-CAL-10) and the first and last day of the stored FYs (FY-03, R-STA-03) are for the
    // Add form, so it needs no extra call (M5, NFR-01).
    // IsAdmin: the email is in ALLOWED_EMAILS, so Settings shows People (AUTH-07).
    // MonthKeyOffset names the month keys of income items and splits in the web app (R-CAL-11).
    public record MeResponse(
        bool SetupCompleted,
        bool IsAdmin,
        ProfileDto? Profile,
        string? CurrentFyId,
        string? CurrentPeriodKey,
        PeriodDto? CurrentPeriod,
        DateOnly? Today,
        DateOnly? FirstDate,
        DateOnly? LastDate,
        int MonthKeyOffset = 0);

    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder api)
    {
        api.MapGet("/me", (ClaimsPrincipal user, IUserStore store, TimeProvider time, EmailAllowlist admins) =>
            LoadAsync(user.FindFirstValue("sub")!, admins.IsAdmin(user), store, time));
        return api;
    }

    // R-CAL-10: "today" in the profile time zone. The current FY and month are null when no
    // stored FY contains today (after the last FY ends, before the next one is started).
    public static async Task<MeResponse> LoadAsync(string uid, bool isAdmin, IUserStore store, TimeProvider time)
    {
        var profile = await store.GetProfileAsync(uid);
        if (profile is null)
        {
            return new MeResponse(SetupCompleted: false, IsAdmin: isAdmin, Profile: null, CurrentFyId: null, CurrentPeriodKey: null,
                CurrentPeriod: null, Today: null, FirstDate: null, LastDate: null);
        }
        var today = FiscalCalendar.Today(time, profile.Timezone);
        var fiscalYears = await store.GetFiscalYearsAsync(uid);
        var current = FiscalCalendar.FindPeriod(fiscalYears, today);
        return new MeResponse(
            profile.SetupCompleted,
            isAdmin,
            ProfileDto.From(profile),
            current?.FiscalYear.Id,
            current?.Period.Key,
            current is { } found ? PeriodDto.From(found.Period) : null,
            today,
            fiscalYears.Count == 0 ? null : fiscalYears.Min(fiscalYear => fiscalYear.StartDate),
            fiscalYears.Count == 0 ? null : fiscalYears.Max(fiscalYear => fiscalYear.EndDate),
            MonthKeyOffset(profile, fiscalYears));
    }

    // R-CAL-11 with the payday of the latest FY, where the newest month keys are (the profile's
    // payday is the next FY's, R-CAL-07).
    public static int MonthKeyOffset(UserProfile profile, IReadOnlyList<FiscalYear> fiscalYears) =>
        FiscalCalendar.KeyOffset(profile.MonthNaming, FiscalCalendar.Latest(fiscalYears)?.PaydayDay ?? profile.PaydayDay);
}
