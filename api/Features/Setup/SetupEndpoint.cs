using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Setup;

// PRD 5.2 setup wizard: the live preview (step 2) and POST /api/setup (SET-02).
public static class SetupEndpoint
{
    public record FiscalYearSummary(string Id, string Label, DateOnly StartDate, DateOnly EndDate);

    public record PreviewResponse(FiscalYearSummary FiscalYear, MeEndpoint.PeriodDto CurrentPeriod);

    public static IEndpointRouteBuilder MapSetup(this IEndpointRouteBuilder api)
    {
        // Owner's choice (M3): the preview comes from FiscalCalendar, so the month and FY rules
        // exist only in api/Domain. Read-only.
        api.MapGet("/setup/preview", (
            [Range(1, 12)] int fiscalStartMonth,
            [Range(1, 31)] int paydayDay,
            string timezone,
            TimeProvider time) =>
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["timezone"] = ["Time zone is unknown."] });
            }
            var today = FiscalCalendar.Today(time, timezone);
            var fiscalYear = FiscalCalendar.FirstFiscalYear(today, fiscalStartMonth, paydayDay);
            return Results.Ok(new PreviewResponse(
                new FiscalYearSummary(fiscalYear.Id, fiscalYear.Label, fiscalYear.StartDate, fiscalYear.EndDate),
                MeEndpoint.PeriodDto.From(FiscalCalendar.PeriodContaining(today, paydayDay))));
        });

        api.MapPost("/setup", async (SetupRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            // AUTH-03: the user id and email come from the token, never from the body.
            var uid = user.FindFirstValue("sub")!;
            if (await store.GetProfileAsync(uid) is not null || !await store.CreateSetupAsync(uid, Build(request, user, time)))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: "Setup is already complete.");
            }
            return Results.Created("/api/me", await MeEndpoint.LoadAsync(uid, store, time));
        });

        return api;
    }

    // R-CAL-05: the first FY contains today in the chosen time zone. Income months (owner's
    // choice, M3): salary and Monthly items run from the FY's first month, open ended; Once
    // items are in the current month.
    private static SetupData Build(SetupRequest request, ClaimsPrincipal user, TimeProvider time)
    {
        var today = FiscalCalendar.Today(time, request.Timezone!);
        var fiscalYear = FiscalCalendar.FirstFiscalYear(today, request.FiscalStartMonth, request.PaydayDay);
        var currentKey = FiscalCalendar.PeriodContaining(today, request.PaydayDay).Key;

        var incomes = new List<IncomeItem>
        {
            new(NewId(), IncomeKind.Salary, IncomeCalculator.SalaryLabel, Money.ToMinor(request.Salary!.Value), fiscalYear.Id, null),
        };
        incomes.AddRange((request.AdditionalIncome ?? []).Select(item => item.Type == AdditionalIncomeRequest.Once
            ? new IncomeItem(NewId(), IncomeKind.Additional, Names.Normalize(item.Label), Money.ToMinor(item.Amount!.Value), currentKey, currentKey)
            : new IncomeItem(NewId(), IncomeKind.Additional, Names.Normalize(item.Label), Money.ToMinor(item.Amount!.Value), fiscalYear.Id, null)));

        var profile = new UserProfile(
            DisplayName: Names.Normalize(request.DisplayName),
            Email: user.FindFirstValue("email")!,
            ProjectName: Names.Normalize(request.ProjectName),
            Currency: request.Currency!,
            Timezone: request.Timezone!,
            FiscalStartMonth: request.FiscalStartMonth,
            PaydayDay: request.PaydayDay,
            SetupCompleted: true,
            LastBankAccountId: null);

        return new SetupData(
            profile,
            fiscalYear,
            incomes,
            request.BankAccounts!.Select(name => new BankAccount(NewId(), Names.Normalize(name))).ToList(),
            request.Categories!.Select((name, i) => new Category(NewId(), Names.Normalize(name), i)).ToList());
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
