using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.BankAccounts;
using Api.Features.FiscalYears;
using Api.Infrastructure;

namespace Api.Features.Income;

// PRD 7.5 income (PRD 4.3, STG-03): salary history and additional income. Every change answers
// with the same body as GET /api/income, so Settings needs one call per change (NFR-01).
public static class IncomeEndpoint
{
    // Kind is "salary" or "additional". Type is "once" (ToPeriodKey equals FromPeriodKey) or
    // "monthly" (ToPeriodKey empty = open ended), R-INC-03. Counts is "planned", "actual" or "both"
    // (R-INC-04). BankAccountId is the account it goes into, null through the split (R-INC-05).
    public record IncomeItemDto(
        string Id,
        string Kind,
        string Label,
        decimal Amount,
        string Type,
        string FromPeriodKey,
        string? ToPeriodKey,
        string Counts,
        string? BankAccountId)
    {
        public static IncomeItemDto From(IncomeItem item) => new(
            item.Id,
            item.Kind == IncomeKind.Salary ? "salary" : "additional",
            item.Label,
            Money.FromMinor(item.AmountMinor),
            item.ToPeriodKey == item.FromPeriodKey ? AdditionalIncomeItemRequest.Once : AdditionalIncomeItemRequest.Monthly,
            item.FromPeriodKey,
            item.ToPeriodKey,
            FirestoreDocuments.ToCountsName(item.Counts),
            item.BankAccountId);
    }

    // R-INC-01: Plan's income and the actual income of one month (owner note 17).
    public record MonthIncomeDto(string PeriodKey, decimal Planned, decimal Actual);

    // FiscalYears (oldest first) give the month pickers; Totals are R-INC-01 for each month of FyId.
    // CurrentTotal (actual) and CurrentPlanned are the month that contains today (STG-03), null when no
    // FY contains today. BankAccounts (archived ones flagged) feed the account picker (R-INC-05).
    public record IncomeResponse(
        IReadOnlyList<FiscalYearsEndpoint.FiscalYearDto> FiscalYears,
        string FyId,
        IReadOnlyList<IncomeItemDto> Salaries,
        IReadOnlyList<IncomeItemDto> Additional,
        IReadOnlyList<MonthIncomeDto> Totals,
        string? CurrentPeriodKey,
        decimal? CurrentTotal,
        decimal? CurrentPlanned,
        IReadOnlyList<BankAccountsEndpoint.BankAccountDto> BankAccounts);

    public static IEndpointRouteBuilder MapIncome(this IEndpointRouteBuilder api)
    {
        // By default the FY that contains today, else the latest (as GET /api/entries). 404 for an unknown FY.
        api.MapGet("/income", async (string? fyId, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
            await LoadAsync(user.FindFirstValue("sub")!, fyId, store, time) is { } income ? Results.Ok(income) : Results.NotFound());

        // R-INC-02 (owner's rule, M2): the salary running into the effective month ends the month
        // before, and salary items starting at or after it are replaced.
        api.MapPost("/income/salary", async (SalaryRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var months = await StoredMonthsAsync(uid, store);
            if (!months.Contains(request.EffectiveFromPeriodKey!))
            {
                return Invalid(nameof(SalaryRequest.EffectiveFromPeriodKey), "Pick a month of a fiscal year.");
            }

            var items = await store.GetIncomesAsync(uid);
            var newId = DocumentIds.New();
            var changed = IncomeCalculator.ChangeSalary(items, newId, Money.ToMinor(request.Amount!.Value), request.EffectiveFromPeriodKey!);
            var before = items.Where(item => item.Kind == IncomeKind.Salary).ToDictionary(item => item.Id);
            var after = changed.Where(item => item.Kind == IncomeKind.Salary).ToDictionary(item => item.Id);
            var ended = after.Values.Where(item => before.TryGetValue(item.Id, out var old) && old.ToPeriodKey != item.ToPeriodKey).ToList();
            var replaced = before.Keys.Where(id => !after.ContainsKey(id)).ToList();

            await store.ChangeSalaryAsync(uid, ended, replaced, after[newId]);
            return Results.Ok(await LoadAsync(uid, null, store, time));
        });

        api.MapPost("/income/additional", async (AdditionalIncomeItemRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (await CheckAsync(uid, request, null, store) is { } error)
            {
                return error;
            }
            var item = request.ToItem(DocumentIds.New());
            await store.AddIncomeAsync(uid, item);
            return Results.Created($"/api/income/additional/{item.Id}", await LoadAsync(uid, null, store, time));
        });

        // R-INC-03: items can be edited and deleted to fix mistakes. A salary item, an unknown item
        // or another user's is 404.
        api.MapPut("/income/additional/{id}", async (string id, AdditionalIncomeItemRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if ((await store.GetIncomesAsync(uid)).FirstOrDefault(item => item.Id == id && item.Kind == IncomeKind.Additional) is not { } current)
            {
                return Results.NotFound();
            }
            if (await CheckAsync(uid, request, current, store) is { } error)
            {
                return error;
            }
            return await store.UpdateIncomeAsync(uid, request.ToItem(id))
                ? Results.Ok(await LoadAsync(uid, null, store, time))
                : Results.NotFound();
        });

        // The confirmation is in the web app (UX-04). Answers 200 with the income, so Settings needs no second call.
        api.MapDelete("/income/additional/{id}", async (string id, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            return await IsAdditionalAsync(uid, id, store) && await store.DeleteIncomeAsync(uid, id)
                ? Results.Ok(await LoadAsync(uid, null, store, time))
                : Results.NotFound();
        });

        return api;
    }

    // Null when the FY is unknown, or when the user has no FY or profile.
    public static async Task<IncomeResponse?> LoadAsync(string uid, string? fyId, IUserStore store, TimeProvider time)
    {
        var profile = await store.GetProfileAsync(uid);
        if (profile is null)
        {
            return null;
        }
        var fiscalYears = (await store.GetFiscalYearsAsync(uid)).OrderBy(fiscalYear => fiscalYear.StartDate).ToList();
        var current = FiscalCalendar.FindPeriod(fiscalYears, FiscalCalendar.Today(time, profile.Timezone));
        var chosen = string.IsNullOrEmpty(fyId)
            ? current?.FiscalYear ?? fiscalYears.LastOrDefault()
            : fiscalYears.FirstOrDefault(fiscalYear => fiscalYear.Id == fyId);
        if (chosen is null)
        {
            return null;
        }

        var items = await store.GetIncomesAsync(uid);
        // Newest first: the salary history reads from the current salary back.
        var ordered = items
            .OrderByDescending(item => item.FromPeriodKey, StringComparer.Ordinal)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var planned = IncomeCalculator.PlannedTotals(items, chosen.Periods);
        var actual = IncomeCalculator.ActualTotals(items, chosen.Periods);
        return new IncomeResponse(
            fiscalYears.Select(FiscalYearsEndpoint.FiscalYearDto.From).ToList(),
            chosen.Id,
            ordered.Where(item => item.Kind == IncomeKind.Salary).Select(IncomeItemDto.From).ToList(),
            ordered.Where(item => item.Kind == IncomeKind.Additional).Select(IncomeItemDto.From).ToList(),
            chosen.Periods
                .Select(period => new MonthIncomeDto(period.Key, Money.FromMinor(planned[period.Key]), Money.FromMinor(actual[period.Key])))
                .ToList(),
            current?.Period.Key,
            current is { } found ? Money.FromMinor(IncomeCalculator.ActualForPeriod(items, found.Period.Key)) : null,
            current is { } again ? Money.FromMinor(IncomeCalculator.PlannedForPeriod(items, again.Period.Key)) : null,
            BankAccountsEndpoint.Sorted(await store.GetBankAccountsAsync(uid)));
    }

    // The pickers offer the months of the stored FYs (R-CAL-09).
    private static async Task<HashSet<string>> StoredMonthsAsync(string uid, IUserStore store) =>
        (await store.GetFiscalYearsAsync(uid)).SelectMany(fiscalYear => fiscalYear.Periods).Select(period => period.Key).ToHashSet(StringComparer.Ordinal);

    // R-INC-05: the account is an active one (as entries and transfers: an edited item may keep the
    // archived account it has).
    private static async Task<IResult?> CheckAsync(string uid, AdditionalIncomeItemRequest request, IncomeItem? current, IUserStore store)
    {
        if (request.AccountId is { } accountId
            && !(await store.GetBankAccountsAsync(uid)).Any(account => account.Id == accountId
                && (!account.Archived || accountId == current?.BankAccountId)))
        {
            return Invalid(nameof(AdditionalIncomeItemRequest.BankAccountId), "Bank account is unknown.");
        }

        var months = await StoredMonthsAsync(uid, store);
        if (!months.Contains(request.FromPeriodKey!))
        {
            return Invalid(nameof(AdditionalIncomeItemRequest.FromPeriodKey), "Pick a month of a fiscal year.");
        }
        if (request.Type == AdditionalIncomeItemRequest.Monthly && !string.IsNullOrEmpty(request.ToPeriodKey) && !months.Contains(request.ToPeriodKey))
        {
            return Invalid(nameof(AdditionalIncomeItemRequest.ToPeriodKey), "Pick a month of a fiscal year.");
        }
        return null;
    }

    private static async Task<bool> IsAdditionalAsync(string uid, string id, IUserStore store) =>
        (await store.GetIncomesAsync(uid)).Any(item => item.Id == id && item.Kind == IncomeKind.Additional);

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

// R-INC-02: a new salary from a month on. A salary of 0 is allowed (as SET-01).
public sealed class SalaryRequest : IValidatableObject
{
    [Required]
    public decimal? Amount { get; init; }

    [Required]
    public string? EffectiveFromPeriodKey { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount is { } amount && !Money.IsValidAmount(amount, allowZero: true))
        {
            yield return new ValidationResult("Salary is 0 to 10,000,000 with at most 2 decimals.", [nameof(Amount)]);
        }
    }
}

// R-INC-03: Once is one month (ToPeriodKey is ignored and stored equal to FromPeriodKey); Monthly
// runs from a month, optionally until a month. Counts is R-INC-04 (missing is both). BankAccountId is
// R-INC-05: ignored for a Planned item (it reaches no account); empty goes through the split.
public sealed class AdditionalIncomeItemRequest : IValidatableObject
{
    public const string Once = "once";
    public const string Monthly = "monthly";

    public string? Label { get; init; }

    [Required]
    public decimal? Amount { get; init; }

    public string? Type { get; init; }

    [Required]
    public string? FromPeriodKey { get; init; }

    public string? ToPeriodKey { get; init; }

    public string? Counts { get; init; }

    public string? BankAccountId { get; init; }

    // The account the item goes into, null for a Planned item or the income split.
    public string? AccountId => Counts == "planned" || string.IsNullOrEmpty(BankAccountId) ? null : BankAccountId;

    public static bool IsCounts(string? counts) => counts is null or "planned" or "actual" or "both";

    public IncomeItem ToItem(string id) => new(
        id,
        IncomeKind.Additional,
        Names.Normalize(Label),
        Money.ToMinor(Amount!.Value),
        FromPeriodKey!,
        Type == Once ? FromPeriodKey : string.IsNullOrEmpty(ToPeriodKey) ? null : ToPeriodKey,
        FirestoreDocuments.ToCounts(Counts),
        AccountId);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // A label is not a name in 4.7, so two items may share one (owner's choice, M3).
        if (!Names.IsValid(Label))
        {
            yield return new ValidationResult("Label is 1 to 40 characters.", [nameof(Label)]);
        }
        if (Amount is { } amount && !Money.IsValidAmount(amount))
        {
            yield return new ValidationResult("Amount is more than 0 and at most 10,000,000, with at most 2 decimals.", [nameof(Amount)]);
        }
        if (Type is not (Once or Monthly))
        {
            yield return new ValidationResult("Type is once or monthly.", [nameof(Type)]);
        }
        if (!IsCounts(Counts))
        {
            yield return new ValidationResult("Counts in is planned, actual or both.", [nameof(Counts)]);
        }
        // Month keys are YYYY-MM, so ordinal order is calendar order.
        if (Type == Monthly && !string.IsNullOrEmpty(ToPeriodKey) && FromPeriodKey is not null
            && string.CompareOrdinal(ToPeriodKey, FromPeriodKey) < 0)
        {
            yield return new ValidationResult("Until is the same month as From or later.", [nameof(ToPeriodKey)]);
        }
    }
}
