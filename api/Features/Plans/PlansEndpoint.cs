using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.FiscalYears;
using Api.Infrastructure;

namespace Api.Features.Plans;

// PRD 7.5 plans of one FY (PRD 5.3). Owner's choice (M4, NFR-01): the response also carries the
// FY's stored months and the PLN-03 totals, so Plan needs only this call and GET /api/categories.
public static class PlansEndpoint
{
    public record PlanDto(string SubCategoryId, string CategoryId, IReadOnlyDictionary<string, decimal> Amounts, decimal Total);

    // PLN-03, R-PLN-03: one row per month.
    public record MonthTotalsDto(string PeriodKey, decimal Planned, decimal Income, decimal Unplanned);

    public record PlansResponse(
        FiscalYearsEndpoint.FiscalYearDto FiscalYear,
        IReadOnlyList<PlanDto> Plans,
        IReadOnlyDictionary<string, decimal> CategoryTotals,
        IReadOnlyList<MonthTotalsDto> Totals);

    public static IEndpointRouteBuilder MapPlans(this IEndpointRouteBuilder api)
    {
        api.MapGet("/fiscal-years/{fyId}/plans", async (string fyId, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var fiscalYear = await FindFiscalYearAsync(uid, fyId, store);
            return fiscalYear is null ? Results.NotFound() : Results.Ok(await LoadAsync(uid, fiscalYear, store));
        });

        // PLN-04, R-STA-02: any month of the FY can be set, closed months included.
        api.MapPut("/fiscal-years/{fyId}/plans/{subCategoryId}", async (
            string fyId, string subCategoryId, PlanRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var fiscalYear = await FindFiscalYearAsync(uid, fyId, store);
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
            return Results.Ok(await LoadAsync(uid, fiscalYear, store));
        });

        return api;
    }

    private static async Task<FiscalYear?> FindFiscalYearAsync(string uid, string fyId, IUserStore store) =>
        (await store.GetFiscalYearsAsync(uid)).FirstOrDefault(fiscalYear => fiscalYear.Id == fyId);

    // R-PLN-02, R-PLN-03 and R-INC-01, over the FY's stored months (R-CAL-09).
    private static async Task<PlansResponse> LoadAsync(string uid, FiscalYear fiscalYear, IUserStore store)
    {
        var plans = await store.GetPlansAsync(uid, fiscalYear.Id);
        var income = IncomeCalculator.TotalsForPeriods(await store.GetIncomesAsync(uid), fiscalYear.Periods);

        var categoryTotals = plans
            .Select(plan => plan.CategoryId)
            .Distinct()
            .ToDictionary(
                categoryId => categoryId,
                categoryId => Money.FromMinor(fiscalYear.Periods.Sum(period => PlanCalculator.CategoryPlanned(plans, categoryId, period.Key))));

        var totals = fiscalYear.Periods.Select(period =>
        {
            var planned = PlanCalculator.MonthPlanned(plans, period.Key);
            return new MonthTotalsDto(period.Key, Money.FromMinor(planned), Money.FromMinor(income[period.Key]),
                Money.FromMinor(PlanCalculator.Unplanned(income[period.Key], planned)));
        }).ToList();

        return new PlansResponse(
            FiscalYearsEndpoint.FiscalYearDto.From(fiscalYear),
            plans.Select(plan => new PlanDto(
                plan.SubCategoryId,
                plan.CategoryId,
                plan.Amounts.ToDictionary(pair => pair.Key, pair => Money.FromMinor(pair.Value)),
                Money.FromMinor(PlanCalculator.Total(plan, fiscalYear.Periods)))).ToList(),
            categoryTotals,
            totals);
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
