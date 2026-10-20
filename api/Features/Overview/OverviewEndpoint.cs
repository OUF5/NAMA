using System.Security.Claims;
using Api.Domain;
using Api.Features.Categories;
using Api.Features.FiscalYears;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Overview;

// PRD 7.5 GET /api/fiscal-years/{fyId}/overview: everything Tracker (PRD 5.6) and Dashboard
// (PRD 5.9) need in one call (NFR-01). Sums are made in minor units (R-MON-01). Owner's choices (M7): remaining
// is planned minus spent, and a sub-category appears when it has a plan or spending in the FY.
// Tracker's filter (owner's choice, 20 Oct 2026): with categoryId, and optionally subCategoryId, the
// rows, months and totals cover only that category or sub-category, and income is null, because
// income belongs to the whole budget. ToDate (Dashboard) always covers everything.
public static class OverviewEndpoint
{
    // One month of one row (Year grid cell, MON-02).
    public record CellDto(string PeriodKey, decimal Planned, decimal Spent);

    public record SubCategoryRowDto(string SubCategoryId, IReadOnlyList<CellDto> Months, decimal Planned, decimal Spent);

    public record CategoryRowDto(
        string CategoryId,
        IReadOnlyList<CellDto> Months,
        decimal Planned,
        decimal Spent,
        IReadOnlyList<SubCategoryRowDto> SubCategories);

    // Status is "closed", "inProgress" or "yetToCome" (R-STA-01), worked out with today in the
    // profile time zone (R-CAL-10).
    public record MonthDto(MeEndpoint.PeriodDto Period, string Status, decimal? Income, decimal Planned, decimal Spent, decimal Remaining);

    public record TotalsDto(decimal? Income, decimal Planned, decimal Spent, decimal Remaining);

    // DSH-02 (owner's choice, M9): the months that started on or before today, each with its full
    // income and every entry in it. Saved is income minus spent and can be negative.
    public record ToDateDto(decimal Income, decimal Spent, decimal Saved);

    // FiscalYears (oldest first) feed the MON-03 selector; Categories is GET /api/categories, for
    // the names and the edit dialog's pickers. Rollover is the FY-01 banner (null when hidden).
    // ToDate is Dashboard's "FY to date" (DSH-02).
    public record OverviewResponse(
        IReadOnlyList<FiscalYearsEndpoint.FiscalYearDto> FiscalYears,
        string FyId,
        IReadOnlyList<MonthDto> Months,
        IReadOnlyList<CategoriesEndpoint.CategoryDto> Categories,
        IReadOnlyList<CategoryRowDto> Rows,
        TotalsDto Totals,
        ToDateDto ToDate,
        FiscalYearsEndpoint.RolloverDto? Rollover);

    public static IEndpointRouteBuilder MapOverview(this IEndpointRouteBuilder api)
    {
        // An unknown FY, or another user's, is 404 (as the plans endpoints, M4). So is an unknown
        // category or sub-category, or a sub-category of another category.
        api.MapGet("/fiscal-years/{fyId}/overview", async (
            string fyId, string? categoryId, string? subCategoryId, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var fiscalYears = (await store.GetFiscalYearsAsync(uid)).OrderBy(fiscalYear => fiscalYear.StartDate).ToList();
            var fiscalYear = fiscalYears.FirstOrDefault(fiscalYear => fiscalYear.Id == fyId);
            var profile = await store.GetProfileAsync(uid);
            if (fiscalYear is null || profile is null)
            {
                return Results.NotFound();
            }
            var filter = string.IsNullOrEmpty(categoryId) && string.IsNullOrEmpty(subCategoryId)
                ? null
                : new Filter(categoryId, string.IsNullOrEmpty(subCategoryId) ? null : subCategoryId);
            var response = await LoadAsync(uid, fiscalYears, fiscalYear, profile, FiscalCalendar.Today(time, profile.Timezone), filter, store);
            return response is null ? Results.NotFound() : Results.Ok(response);
        });
        return api;
    }

    private sealed record Filter(string? CategoryId, string? SubCategoryId);

    // Null when the filter names an unknown category or sub-category.
    private static async Task<OverviewResponse?> LoadAsync(
        string uid, IReadOnlyList<FiscalYear> fiscalYears, FiscalYear fiscalYear, UserProfile profile, DateOnly today,
        Filter? filter, IUserStore store)
    {
        var periods = fiscalYear.Periods;
        var allPlans = await store.GetPlansAsync(uid, fiscalYear.Id);
        var income = IncomeCalculator.TotalsForPeriods(await store.GetIncomesAsync(uid), periods);
        // PRD 7.4 "Reads": the FY's entries in one query (the Entries query), added up in memory.
        var allEntries = await store.GetFiscalYearEntriesAsync(uid, fiscalYear.Id);
        var categories = (await store.GetCategoriesAsync(uid)).OrderBy(category => category.SortOrder).ToList();
        var subCategories = await store.GetSubCategoriesAsync(uid);

        // The filter's sub-categories; a sub-category alone also picks its category.
        HashSet<string>? scope = null;
        if (filter is not null)
        {
            var sub = filter.SubCategoryId is null ? null : subCategories.FirstOrDefault(item => item.Id == filter.SubCategoryId);
            var categoryId = filter.CategoryId ?? sub?.CategoryId;
            if (categories.All(category => category.Id != categoryId)
                || (filter.SubCategoryId is not null && (sub is null || sub.CategoryId != categoryId)))
            {
                return null;
            }
            scope = subCategories
                .Where(item => item.CategoryId == categoryId && (sub is null || item.Id == sub.Id))
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
        }
        var plans = scope is null ? allPlans : allPlans.Where(plan => scope.Contains(plan.SubCategoryId)).ToList();
        var entries = scope is null ? allEntries : allEntries.Where(entry => scope.Contains(entry.SubCategoryId)).ToList();

        var spent = entries
            .GroupBy(entry => (entry.SubCategoryId, entry.PeriodKey))
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.AmountMinor));
        var plansBySub = plans.ToDictionary(plan => plan.SubCategoryId);

        IReadOnlyList<(string PeriodKey, long Planned, long Spent)> SubCells(SubCategory sub) => periods
            .Select(period => (
                PeriodKey: period.Key,
                Planned: plansBySub.TryGetValue(sub.Id, out var plan) ? PlanCalculator.AmountFor(plan, period.Key) : 0L,
                Spent: spent.GetValueOrDefault((sub.Id, period.Key))))
            .ToList();

        var rows = new List<CategoryRowDto>();
        foreach (var category in categories)
        {
            // Owner's choice (M7): only sub-categories with a plan or spending in the FY, archived or not.
            var subRows = subCategories
                .Where(sub => sub.CategoryId == category.Id && (scope is null || scope.Contains(sub.Id)))
                .OrderBy(sub => sub.SortOrder)
                .Select(sub => (Sub: sub, Cells: SubCells(sub)))
                .Where(row => row.Cells.Any(cell => cell.Planned > 0 || cell.Spent > 0))
                .ToList();
            if (subRows.Count == 0)
            {
                continue;
            }
            // R-PLN-02: category planned = sum of its sub-categories; spent likewise.
            var categoryCells = periods
                .Select((period, i) => (
                    PeriodKey: period.Key,
                    Planned: subRows.Sum(row => row.Cells[i].Planned),
                    Spent: subRows.Sum(row => row.Cells[i].Spent)))
                .ToList();
            rows.Add(new CategoryRowDto(
                category.Id,
                Cells(categoryCells),
                Money.FromMinor(categoryCells.Sum(cell => cell.Planned)),
                Money.FromMinor(categoryCells.Sum(cell => cell.Spent)),
                subRows.Select(row => new SubCategoryRowDto(
                    row.Sub.Id,
                    Cells(row.Cells),
                    Money.FromMinor(row.Cells.Sum(cell => cell.Planned)),
                    Money.FromMinor(row.Cells.Sum(cell => cell.Spent)))).ToList()));
        }

        // Month and FY spent count every entry, so they equal the Entries totals (M7 "Done when").
        var months = periods.Select(period =>
        {
            var planned = PlanCalculator.MonthPlanned(plans, period.Key);
            var monthSpent = entries.Where(entry => entry.PeriodKey == period.Key).Sum(entry => entry.AmountMinor);
            return (Period: period, Planned: planned, Spent: monthSpent);
        }).ToList();
        var totalPlanned = months.Sum(month => month.Planned);
        var totalSpent = entries.Sum(entry => entry.AmountMinor);
        // DSH-02 covers everything, whatever the filter.
        var started = periods.Where(period => FiscalCalendar.HasStarted(period, today)).Select(period => period.Key).ToHashSet(StringComparer.Ordinal);
        var incomeToDate = started.Sum(key => income[key]);
        var spentToDate = allEntries.Where(entry => started.Contains(entry.PeriodKey)).Sum(entry => entry.AmountMinor);

        return new OverviewResponse(
            fiscalYears.Select(FiscalYearsEndpoint.FiscalYearDto.From).ToList(),
            fiscalYear.Id,
            months.Select(month => new MonthDto(
                MeEndpoint.PeriodDto.From(month.Period),
                StatusName(FiscalCalendar.Status(month.Period, today)),
                scope is null ? Money.FromMinor(income[month.Period.Key]) : null,
                Money.FromMinor(month.Planned),
                Money.FromMinor(month.Spent),
                Money.FromMinor(PlanCalculator.Remaining(month.Planned, month.Spent)))).ToList(),
            categories.Select(category => CategoriesEndpoint.ToDto(category, subCategories)).ToList(),
            rows,
            new TotalsDto(
                scope is null ? Money.FromMinor(income.Values.Sum()) : null,
                Money.FromMinor(totalPlanned),
                Money.FromMinor(totalSpent),
                Money.FromMinor(PlanCalculator.Remaining(totalPlanned, totalSpent))),
            new ToDateDto(
                Money.FromMinor(incomeToDate),
                Money.FromMinor(spentToDate),
                Money.FromMinor(PlanCalculator.Saved(incomeToDate, spentToDate))),
            FiscalYearsEndpoint.Rollover(fiscalYears, profile, today));
    }

    private static List<CellDto> Cells(IEnumerable<(string PeriodKey, long Planned, long Spent)> cells) =>
        cells.Select(cell => new CellDto(cell.PeriodKey, Money.FromMinor(cell.Planned), Money.FromMinor(cell.Spent))).ToList();

    private static string StatusName(PeriodStatus status) => status switch
    {
        PeriodStatus.Closed => "closed",
        PeriodStatus.InProgress => "inProgress",
        _ => "yetToCome",
    };
}
