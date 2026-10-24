using System.Security.Claims;
using Api.Domain;
using Api.Features.Categories;
using Api.Features.FiscalYears;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Overview;

// PRD 7.5 GET /api/fiscal-years/{fyId}/overview: everything Tracker (PRD 5.6) and Dashboard
// (PRD 5.9) need in one call (NFR-01). Sums are made in minor units (R-MON-01). A sub-category appears
// when it has a plan or spending in the FY (owner's choice, M7). R-PLN-05 (20 Oct 2026): what is left
// carries into the next month, so remaining = carried in + planned - spent, from 0 at the FY start.
// Tracker's filter (owner's choice, 20 Oct 2026): with categoryId, and optionally subCategoryId, the
// rows, months and totals cover only that category or sub-category, and income is null, because
// income belongs to the whole budget. ToDate (Dashboard) always covers everything.
public static class OverviewEndpoint
{
    // One month of one row (Year grid cell, MON-02). Available is CarriedIn + Planned (R-PLN-05).
    public record CellDto(string PeriodKey, decimal CarriedIn, decimal Planned, decimal Spent, decimal Remaining);

    public record SubCategoryRowDto(string SubCategoryId, IReadOnlyList<CellDto> Months, decimal Planned, decimal Spent);

    public record CategoryRowDto(
        string CategoryId,
        IReadOnlyList<CellDto> Months,
        decimal Planned,
        decimal Spent,
        IReadOnlyList<SubCategoryRowDto> SubCategories);

    // Status is "closed", "inProgress" or "yetToCome" (R-STA-01), worked out with today in the
    // profile time zone (R-CAL-10). Income is the actual income and PlannedIncome Plan's (R-INC-01,
    // owner note 17); both null with a filter. AdditionalIncome is the part of Income from additional
    // items (Actual and Both, R-INC-04), null with a filter (owner note 23). Unspent is the month's income minus its spent and Saved
    // that added up from the FY's first month (owner note 18, Tracker's Totals); both null with a
    // filter and for a month yet to come. ProjectedUnspent and ProjectedSaved are the same carried on to
    // the FY's end (owner note 23, PlanCalculator.ProjectSaved): for the month in progress and the months
    // yet to come, null for a closed month and with a filter.
    public record MonthDto(
        MeEndpoint.PeriodDto Period, string Status, decimal? Income, decimal? PlannedIncome, decimal? AdditionalIncome, decimal CarriedIn, decimal Planned,
        decimal Spent, decimal Remaining, decimal? Unspent, decimal? Saved, decimal? ProjectedUnspent, decimal? ProjectedSaved);

    // The FY: remaining is FY planned - FY spent (the last month's remaining). PlannedSaved is the
    // last month's Unassigned (R-PLN-04, planned income), null with a filter like income. Saved is the
    // latest started month's Saved (owner note 18), null with a filter or before the FY starts.
    // ProjectedSaved is the accumulated savings projected at the FY's end (owner note 23), null with a
    // filter or once every month is closed.
    public record TotalsDto(
        decimal? Income, decimal? PlannedIncome, decimal? AdditionalIncome, decimal Planned, decimal Spent, decimal Remaining, decimal? PlannedSaved, decimal? Saved,
        decimal? ProjectedSaved);

    // DSH-02 (owner's choice, M9): the months that started on or before today, each with its full
    // actual income (R-INC-01) and every entry in it, from the first month that carries (R-PLN-06: months before NAMA was
    // set up were never tracked). Saved is income minus spent and can be negative. PlannedSaved is the
    // Unassigned of the latest started month (R-PLN-03): what the plan meant to keep by now.
    // Planned is the started months' planned spending (DSH-02 "planned until now"). SavedClosed is
    // income minus spent of the closed months only: what the FY added to the banks (DSH-04).
    public record ToDateDto(decimal Income, decimal Planned, decimal Spent, decimal Saved, decimal PlannedSaved, decimal SavedClosed);

    // Owner note 24, Dashboard's "Plan vs actual" (DSH-02) and "Saving: plan vs projection" (DSH-06),
    // from the FY's first month like Tracker's Totals and Plan's Allocating. The started months (the
    // one in progress in full): PlannedIncome and Income (actual, AdditionalIncome the part from
    // additional items), Planned and Spent, PlannedSaved (Plan's Accumulated Planned Savings, R-PLN-03)
    // and Saved (Tracker's Accumulated savings). StillToSpend is what is left of the month in progress's
    // plan (R-PLN-07). At the FY's end: PlannedSavedAtEnd (Plan's) and ProjectedSaved (Tracker's, owner
    // note 23; Saved once every month is closed), which is Saved - StillToSpend + FutureUnspent.
    public record PlanVsActualDto(
        decimal PlannedIncome, decimal Income, decimal AdditionalIncome, decimal Planned, decimal Spent, decimal StillToSpend,
        decimal PlannedSaved, decimal Saved, decimal FutureUnspent, decimal PlannedSavedAtEnd, decimal ProjectedSaved);

    // FiscalYears (oldest first) feed the MON-03 selector; Categories is GET /api/categories, for
    // the names and the edit dialog's pickers. Rollover is the FY-01 banner (null when hidden).
    // ToDate is Dashboard's "FY to date" (DSH-02). CarryFromPeriodKey is the first month that carries
    // (R-PLN-06), null when none of this FY's months does. PlanVsActual is null with a filter.
    public record OverviewResponse(
        IReadOnlyList<FiscalYearsEndpoint.FiscalYearDto> FiscalYears,
        string FyId,
        IReadOnlyList<MonthDto> Months,
        IReadOnlyList<CategoriesEndpoint.CategoryDto> Categories,
        IReadOnlyList<CategoryRowDto> Rows,
        TotalsDto Totals,
        ToDateDto ToDate,
        PlanVsActualDto? PlanVsActual,
        FiscalYearsEndpoint.RolloverDto? Rollover,
        string? CarryFromPeriodKey);

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
        // R-PLN-06: the carry starts in the month NAMA was set up, or the FY's first month.
        var carryStart = PlanCalculator.CarryStart(periods, profile.StartedOn);
        var allPlans = await store.GetPlansAsync(uid, fiscalYear.Id);
        // R-INC-01 (owner note 17): Unassigned is the plan's, so it uses the planned income; the months,
        // the totals and FY to date show what came in, the actual income.
        var incomes = await store.GetIncomesAsync(uid);
        var plannedIncome = IncomeCalculator.PlannedTotals(incomes, periods);
        var income = IncomeCalculator.ActualTotals(incomes, periods);
        // Owner note 23: the part of it from additional items.
        var additionalIncome = IncomeCalculator.ActualTotals(incomes.Where(item => item.Kind == IncomeKind.Additional).ToList(), periods);
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

        // R-PLN-05: each sub-category carries on its own; categories and months are their sums.
        IReadOnlyList<CarriedMonth> SubCells(SubCategory sub) => PlanCalculator.Carry(periods
            .Select(period => (
                plansBySub.TryGetValue(sub.Id, out var plan) ? PlanCalculator.AmountFor(plan, period.Key) : 0L,
                spent.GetValueOrDefault((sub.Id, period.Key)))), carryStart);

        List<CellDto> Cells(IReadOnlyList<CarriedMonth> cells) => cells
            .Select((cell, i) => new CellDto(periods[i].Key, Money.FromMinor(cell.CarriedIn), Money.FromMinor(cell.Planned),
                Money.FromMinor(cell.Spent), Money.FromMinor(cell.Remaining)))
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
            // R-PLN-02: category planned = sum of its sub-categories; spent and carried likewise.
            var categoryCells = periods
                .Select((_, i) => new CarriedMonth(
                    subRows.Sum(row => row.Cells[i].CarriedIn),
                    subRows.Sum(row => row.Cells[i].Planned),
                    subRows.Sum(row => row.Cells[i].Spent)))
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
        var months = PlanCalculator.Carry(periods.Select(period => (
            PlanCalculator.MonthPlanned(plans, period.Key),
            entries.Where(entry => entry.PeriodKey == period.Key).Sum(entry => entry.AmountMinor))), carryStart);
        var totalPlanned = months.Sum(month => month.Planned);
        var totalSpent = entries.Sum(entry => entry.AmountMinor);
        // R-PLN-03, R-PLN-04 over the whole budget (a filter has no income).
        var unassigned = PlanCalculator.RunningUnassigned(periods, plannedIncome, allPlans, carryStart);
        // DSH-02 covers everything, whatever the filter, from the first month that carries.
        var started = periods.Skip(carryStart).Where(period => FiscalCalendar.HasStarted(period, today)).Select(period => period.Key).ToHashSet(StringComparer.Ordinal);
        var incomeToDate = started.Sum(key => income[key]);
        var spentToDate = allEntries.Where(entry => started.Contains(entry.PeriodKey)).Sum(entry => entry.AmountMinor);
        var plannedSavedToDate = unassigned.LastOrDefault(month => started.Contains(month.PeriodKey))?.Unassigned ?? 0;
        var plannedToDate = unassigned.Where(month => started.Contains(month.PeriodKey)).Sum(month => month.Planned);
        var closed = periods.Skip(carryStart).Where(period => FiscalCalendar.Status(period, today) == PeriodStatus.Closed)
            .Select(period => period.Key).ToHashSet(StringComparer.Ordinal);
        var savedClosed = PlanCalculator.Saved(
            closed.Sum(key => income[key]), allEntries.Where(entry => closed.Contains(entry.PeriodKey)).Sum(entry => entry.AmountMinor));
        // Owner note 18: the started months' income minus spent, added up from the FY's first month.
        var savedMonths = PlanCalculator.RunningSaved(periods
            .Select((period, i) => (period, i))
            .Where(month => FiscalCalendar.HasStarted(month.period, today))
            .Select(month => (month.period.Key, income[month.period.Key], months[month.i].Spent)));
        var saved = savedMonths.ToDictionary(month => month.PeriodKey);
        decimal? Saved(string periodKey, Func<SavedMonth, long> value) =>
            scope is null && saved.TryGetValue(periodKey, out var month) ? Money.FromMinor(value(month)) : null;
        // Owner note 23: the month in progress spends the rest of its whole-budget plan (R-PLN-07), and
        // each month yet to come gets its actual income minus its planned.
        var current = periods.ToList().FindIndex(period => FiscalCalendar.Status(period, today) == PeriodStatus.InProgress);
        var stillToSpend = current >= 0 ? Math.Max(0, months[current].Remaining) : 0;
        var projectedMonths = PlanCalculator.ProjectSaved(
            savedMonths,
            current >= 0,
            stillToSpend,
            periods.Select((period, i) => (period, i))
                .Where(month => FiscalCalendar.Status(month.period, today) == PeriodStatus.YetToCome)
                .Select(month => (month.period.Key, income[month.period.Key], unassigned[month.i].Planned)));
        var projected = projectedMonths.ToDictionary(month => month.PeriodKey);
        decimal? Projected(string periodKey, Func<SavedMonth, long> value) =>
            scope is null && projected.TryGetValue(periodKey, out var month) ? Money.FromMinor(value(month)) : null;

        // Owner note 24: the started months from the FY's first month, and Plan's running Unassigned
        // from there too (owner note 10), whatever month NAMA was set up in.
        var startedFromFirst = periods.Select((period, i) => (period, i)).Where(month => FiscalCalendar.HasStarted(month.period, today)).ToList();
        var planFromFirst = PlanCalculator.RunningUnassigned(periods, plannedIncome, allPlans);
        var savedSoFar = savedMonths.Count > 0 ? savedMonths[^1].Saved : 0;
        var planVsActual = scope is null
            ? new PlanVsActualDto(
                Money.FromMinor(startedFromFirst.Sum(month => plannedIncome[month.period.Key])),
                Money.FromMinor(startedFromFirst.Sum(month => income[month.period.Key])),
                Money.FromMinor(startedFromFirst.Sum(month => additionalIncome[month.period.Key])),
                Money.FromMinor(startedFromFirst.Sum(month => months[month.i].Planned)),
                Money.FromMinor(startedFromFirst.Sum(month => months[month.i].Spent)),
                Money.FromMinor(stillToSpend),
                Money.FromMinor(startedFromFirst.Count > 0 ? planFromFirst[startedFromFirst[^1].i].Unassigned : 0),
                Money.FromMinor(savedSoFar),
                Money.FromMinor(projectedMonths.Where(month => !saved.ContainsKey(month.PeriodKey)).Sum(month => month.ThisMonth)),
                Money.FromMinor(PlanCalculator.PlannedSaved(planFromFirst)),
                Money.FromMinor(projectedMonths.Count > 0 ? projectedMonths[^1].Saved : savedSoFar))
            : null;

        return new OverviewResponse(
            fiscalYears.Select(FiscalYearsEndpoint.FiscalYearDto.From).ToList(),
            fiscalYear.Id,
            periods.Select((period, i) => new MonthDto(
                MeEndpoint.PeriodDto.From(period),
                StatusName(FiscalCalendar.Status(period, today)),
                scope is null ? Money.FromMinor(income[period.Key]) : null,
                scope is null ? Money.FromMinor(plannedIncome[period.Key]) : null,
                scope is null ? Money.FromMinor(additionalIncome[period.Key]) : null,
                Money.FromMinor(months[i].CarriedIn),
                Money.FromMinor(months[i].Planned),
                Money.FromMinor(months[i].Spent),
                Money.FromMinor(months[i].Remaining),
                Saved(period.Key, month => month.ThisMonth),
                Saved(period.Key, month => month.Saved),
                Projected(period.Key, month => month.ThisMonth),
                Projected(period.Key, month => month.Saved))).ToList(),
            categories.Select(category => CategoriesEndpoint.ToDto(category, subCategories)).ToList(),
            rows,
            new TotalsDto(
                scope is null ? Money.FromMinor(income.Values.Sum()) : null,
                scope is null ? Money.FromMinor(plannedIncome.Values.Sum()) : null,
                scope is null ? Money.FromMinor(additionalIncome.Values.Sum()) : null,
                Money.FromMinor(totalPlanned),
                Money.FromMinor(totalSpent),
                Money.FromMinor(PlanCalculator.Remaining(totalPlanned, totalSpent)),
                scope is null ? Money.FromMinor(PlanCalculator.PlannedSaved(unassigned)) : null,
                scope is null && savedMonths.Count > 0 ? Money.FromMinor(savedMonths[^1].Saved) : null,
                scope is null && projectedMonths.Count > 0 ? Money.FromMinor(projectedMonths[^1].Saved) : null),
            new ToDateDto(
                Money.FromMinor(incomeToDate),
                Money.FromMinor(plannedToDate),
                Money.FromMinor(spentToDate),
                Money.FromMinor(PlanCalculator.Saved(incomeToDate, spentToDate)),
                Money.FromMinor(plannedSavedToDate),
                Money.FromMinor(savedClosed)),
            planVsActual,
            FiscalYearsEndpoint.Rollover(fiscalYears, profile, today),
            carryStart < periods.Count ? periods[carryStart].Key : null);
    }

    private static string StatusName(PeriodStatus status) => status switch
    {
        PeriodStatus.Closed => "closed",
        PeriodStatus.InProgress => "inProgress",
        _ => "yetToCome",
    };
}
