using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.FiscalYears;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Setup;

// PRD 5.2 setup wizard: the live preview (step 2) and POST /api/setup (SET-02).
public static class SetupEndpoint
{
    // The FY's months give step 3's income month pickers (R-INC-03). MonthKeyOffset names month keys
    // as /api/me's does (R-CAL-11).
    public record PreviewResponse(FiscalYearsEndpoint.FiscalYearDto FiscalYear, MeEndpoint.PeriodDto CurrentPeriod, int MonthKeyOffset);

    public static IEndpointRouteBuilder MapSetup(this IEndpointRouteBuilder api)
    {
        // Owner's choice (M3): the preview comes from FiscalCalendar, so the month and FY rules
        // exist only in api/Domain. Read-only.
        api.MapGet("/setup/preview", (
            [Range(1, 12)] int fiscalStartMonth,
            [Range(1, 31)] int paydayDay,
            string timezone,
            [AllowedValues(null, "end", "payday")] string? monthNaming,
            TimeProvider time) =>
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["timezone"] = ["Time zone is unknown."] });
            }
            var today = FiscalCalendar.Today(time, timezone);
            var naming = FirestoreDocuments.ToMonthNaming(monthNaming);
            var fiscalYear = FiscalCalendar.Named(FiscalCalendar.FirstFiscalYear(today, fiscalStartMonth, paydayDay), naming);
            return Results.Ok(new PreviewResponse(
                FiscalYearsEndpoint.FiscalYearDto.From(fiscalYear),
                MeEndpoint.PeriodDto.From(FiscalCalendar.Named(FiscalCalendar.PeriodContaining(today, paydayDay), naming)),
                FiscalCalendar.KeyOffset(naming, paydayDay)));
        });

        api.MapPost("/setup", async (SetupRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time, EmailAllowlist admins) =>
        {
            // AUTH-03: the user id and email come from the token, never from the body.
            var uid = user.FindFirstValue("sub")!;
            // R-CAL-05: the first FY contains today in the chosen time zone. R-CAL-11: named as chosen.
            var fiscalYear = FiscalCalendar.Named(
                FiscalCalendar.FirstFiscalYear(FiscalCalendar.Today(time, request.Timezone!), request.FiscalStartMonth, request.PaydayDay),
                FirestoreDocuments.ToMonthNaming(request.MonthNaming));
            if (MonthErrors(request, fiscalYear) is { Count: > 0 } errors)
            {
                return Results.ValidationProblem(errors);
            }
            var today = FiscalCalendar.Today(time, request.Timezone!);
            if (await store.GetProfileAsync(uid) is not null || !await store.CreateSetupAsync(uid, Build(request, user, fiscalYear, today)))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: "Setup is already complete.");
            }
            return Results.Created("/api/me", await MeEndpoint.LoadAsync(uid, admins.IsAdmin(user), store, time));
        });

        return api;
    }

    // R-INC-03: every additional income month is a month of the first FY. The wizard offers
    // only those, but the FY can change between the preview and Finish (a new day).
    private static Dictionary<string, string[]> MonthErrors(SetupRequest request, FiscalYear fiscalYear)
    {
        var months = fiscalYear.Periods.Select(period => period.Key).ToHashSet(StringComparer.Ordinal);
        var errors = new Dictionary<string, string[]>();
        var items = request.AdditionalIncome ?? [];
        for (var i = 0; i < items.Count; i++)
        {
            if (!months.Contains(items[i].FromPeriodKey!))
            {
                errors[$"{nameof(SetupRequest.AdditionalIncome)}[{i}].{nameof(AdditionalIncomeRequest.FromPeriodKey)}"] = ["Pick a month of the fiscal year."];
            }
            if (items[i].Type == AdditionalIncomeRequest.Monthly && !string.IsNullOrEmpty(items[i].ToPeriodKey) && !months.Contains(items[i].ToPeriodKey!))
            {
                errors[$"{nameof(SetupRequest.AdditionalIncome)}[{i}].{nameof(AdditionalIncomeRequest.ToPeriodKey)}"] = ["Pick a month of the fiscal year."];
            }
        }
        return errors;
    }

    // The salary runs from the FY's first month, open ended (owner's choice, M3). Additional
    // income uses the chosen months: Once is stored with to = from (R-INC-03), and counts in what was
    // picked (R-INC-04). Each account opens
    // with today's balance (R-BAL-01), and the split applies from the FY's first month (R-BAL-02).
    private static SetupData Build(SetupRequest request, ClaimsPrincipal user, FiscalYear fiscalYear, DateOnly today)
    {
        var incomes = new List<IncomeItem>
        {
            new(DocumentIds.New(), IncomeKind.Salary, IncomeCalculator.SalaryLabel, Money.ToMinor(request.Salary!.Value), fiscalYear.Id, null),
        };
        incomes.AddRange((request.AdditionalIncome ?? []).Select(item => new IncomeItem(
            DocumentIds.New(),
            IncomeKind.Additional,
            Names.Normalize(item.Label),
            Money.ToMinor(item.Amount!.Value),
            item.FromPeriodKey!,
            item.Type == AdditionalIncomeRequest.Once ? item.FromPeriodKey : string.IsNullOrEmpty(item.ToPeriodKey) ? null : item.ToPeriodKey,
            FirestoreDocuments.ToCounts(item.Counts))));

        var profile = new UserProfile(
            DisplayName: Names.Normalize(request.DisplayName),
            Email: user.FindFirstValue("email")!,
            ProjectName: Names.Normalize(request.ProjectName),
            Currency: request.Currency!,
            Timezone: request.Timezone!,
            FiscalStartMonth: request.FiscalStartMonth,
            PaydayDay: request.PaydayDay,
            SetupCompleted: true,
            LastBankAccountId: null,
            MonthNaming: FirestoreDocuments.ToMonthNaming(request.MonthNaming));

        var accounts = request.BankAccounts!
            .Select(account => (Account: new BankAccount(DocumentIds.New(), Names.Normalize(account.Name), Opening:
                new OpeningBalance(Money.ToMinor(account.OpeningBalance!.Value), today)), account.Share!.Value))
            .ToList();
        return new SetupData(
            profile,
            fiscalYear,
            incomes,
            accounts.Select(item => item.Account).ToList(),
            request.Categories!.Select((name, i) => new Category(DocumentIds.New(), Names.Normalize(name), i)).ToList(),
            new IncomeSplit(fiscalYear.Id, accounts
                .Where(item => item.Value > 0)
                .ToDictionary(item => item.Account.Id, item => Money.ToBasisPoints(item.Value))));
    }
}
