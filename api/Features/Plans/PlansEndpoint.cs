using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.FiscalYears;
using Api.Infrastructure;

namespace Api.Features.Plans;

// PRD 7.5 plans of one FY (PRD 5.3). Owner's choice (M4, NFR-01): the response also carries the
// FY's stored months and the PLN-03 totals, so Plan needs only this call and GET /api/categories.
// FiscalYears (oldest first) feed Plan's FY selector (owner's choice, M8).
public static class PlansEndpoint
{
    public record PlanDto(string SubCategoryId, string CategoryId, IReadOnlyDictionary<string, decimal> Amounts, decimal Total);

    // PLN-03, R-PLN-03: one row per month. ThisMonth is the month on its own (Income - Planned, not
    // carried); CarriedIn is the previous month's Unassigned, so Unassigned = ThisMonth + CarriedIn.
    public record MonthTotalsDto(
        string PeriodKey, decimal CarriedIn, decimal Income, decimal Planned, decimal ThisMonth, decimal Unassigned);

    // The FY column of PLN-03, from the first month that carries (R-PLN-06) to the FY's end, so it adds
    // up: Income - Planned = PlannedSaved, the last month's Unassigned (R-PLN-04).
    public record FiscalYearTotalsDto(decimal Income, decimal Planned, decimal PlannedSaved);

    // CategoryMonthTotals: category id → month key → planned (R-PLN-02), for Plan's month
    // column (owner's choice, M8), so the browser does no sums.
    public record PlansResponse(
        IReadOnlyList<FiscalYearsEndpoint.FiscalYearDto> FiscalYears,
        FiscalYearsEndpoint.FiscalYearDto FiscalYear,
        IReadOnlyList<PlanDto> Plans,
        IReadOnlyDictionary<string, decimal> CategoryTotals,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> CategoryMonthTotals,
        IReadOnlyList<MonthTotalsDto> Totals,
        FiscalYearTotalsDto FiscalYearTotals,
        string? CarryFromPeriodKey);

    public static IEndpointRouteBuilder MapPlans(this IEndpointRouteBuilder api)
    {
        api.MapGet("/fiscal-years/{fyId}/plans", async (string fyId, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var fiscalYears = await FiscalYearsAsync(uid, store);
            var fiscalYear = fiscalYears.FirstOrDefault(fiscalYear => fiscalYear.Id == fyId);
            return fiscalYear is null ? Results.NotFound() : Results.Ok(await LoadAsync(uid, fiscalYears, fiscalYear, store));
        });

        // PLN-04, R-STA-02: any month of the FY can be set, closed months included.
        api.MapPut("/fiscal-years/{fyId}/plans/{subCategoryId}", async (
            string fyId, string subCategoryId, PlanRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var fiscalYears = await FiscalYearsAsync(uid, store);
            var fiscalYear = fiscalYears.FirstOrDefault(fiscalYear => fiscalYear.Id == fyId);
            var subCategory = (await store.GetSubCategoriesAsync(uid)).FirstOrDefault(sub => sub.Id == subCategoryId);
            if (fiscalYear is null || subCategory is null)
            {
                return Results.NotFound();
            }

            // Owner's choice (M4): 0 and blank both clear a month (R-PLN-01).
            var amounts = PlanCalculator.NormalizeAmounts(
                request.Amounts!.ToDictionary(pair => pair.Key, pair => pair.Value is { } amount ? Money.ToMinor(amount) : 0L),
                fiscalYear.Periods);
            if (amounts is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(PlanRequest.Amounts)] = [$"Every month is one of the months of {fiscalYear.Label}."],
                });
            }

            await store.SetPlanAsync(uid, fiscalYear.Id, new PlanAmounts(subCategory.Id, subCategory.CategoryId, amounts));
            return Results.Ok(await LoadAsync(uid, fiscalYears, fiscalYear, store));
        });

        return api;
    }

    private static async Task<IReadOnlyList<FiscalYear>> FiscalYearsAsync(string uid, IUserStore store) =>
        (await store.GetFiscalYearsAsync(uid)).OrderBy(fiscalYear => fiscalYear.StartDate).ToList();

    // R-PLN-02 to R-PLN-04 and R-INC-01, over the FY's stored months (R-CAL-09).
    private static async Task<PlansResponse> LoadAsync(string uid, IReadOnlyList<FiscalYear> fiscalYears, FiscalYear fiscalYear, IUserStore store)
    {
        var plans = await store.GetPlansAsync(uid, fiscalYear.Id);
        var income = IncomeCalculator.TotalsForPeriods(await store.GetIncomesAsync(uid), fiscalYear.Periods);

        var categoryIds = plans.Select(plan => plan.CategoryId).Distinct().ToList();
        var categoryTotals = categoryIds.ToDictionary(
            categoryId => categoryId,
            categoryId => Money.FromMinor(fiscalYear.Periods.Sum(period => PlanCalculator.CategoryPlanned(plans, categoryId, period.Key))));
        var categoryMonthTotals = categoryIds.ToDictionary(
            categoryId => categoryId,
            categoryId => (IReadOnlyDictionary<string, decimal>)fiscalYear.Periods.ToDictionary(
                period => period.Key,
                period => Money.FromMinor(PlanCalculator.CategoryPlanned(plans, categoryId, period.Key))));

        // R-PLN-06: the carry starts in the month NAMA was set up, or the FY's first month.
        var carryStart = PlanCalculator.CarryStart(fiscalYear.Periods, (await store.GetProfileAsync(uid))?.StartedOn);
        var months = PlanCalculator.RunningUnassigned(fiscalYear.Periods, income, plans, carryStart);
        var counted = months.Skip(carryStart).ToList();
        var totals = months.Select(month => new MonthTotalsDto(
            month.PeriodKey,
            Money.FromMinor(month.CarriedIn),
            Money.FromMinor(month.Income),
            Money.FromMinor(month.Planned),
            Money.FromMinor(month.ThisMonth),
            Money.FromMinor(month.Unassigned))).ToList();

        return new PlansResponse(
            fiscalYears.Select(FiscalYearsEndpoint.FiscalYearDto.From).ToList(),
            FiscalYearsEndpoint.FiscalYearDto.From(fiscalYear),
            plans.Select(plan => new PlanDto(
                plan.SubCategoryId,
                plan.CategoryId,
                plan.Amounts.ToDictionary(pair => pair.Key, pair => Money.FromMinor(pair.Value)),
                Money.FromMinor(PlanCalculator.Total(plan, fiscalYear.Periods)))).ToList(),
            categoryTotals,
            categoryMonthTotals,
            totals,
            new FiscalYearTotalsDto(
                Money.FromMinor(counted.Sum(month => month.Income)),
                Money.FromMinor(counted.Sum(month => month.Planned)),
                Money.FromMinor(PlanCalculator.PlannedSaved(months))),
            carryStart < fiscalYear.Periods.Count ? fiscalYear.Periods[carryStart].Key : null);
    }
}

// PUT body: month key → amount (decimal, R-MON-01). Missing or null months are cleared.
public sealed class PlanRequest : IValidatableObject
{
    [Required]
    public Dictionary<string, decimal?>? Amounts { get; init; }

    // R-MON-02, with 0 allowed to clear a month (owner's choice, M4).
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (key, amount) in Amounts ?? [])
        {
            if (amount is { } value && !Money.IsValidAmount(value, allowZero: true))
            {
                yield return new ValidationResult(
                    "Amount is 0 to 10,000,000 with at most 2 decimals.", [$"{nameof(Amounts)}[{key}]"]);
            }
        }
    }
}
