using Api.Domain;

namespace Api.Tests;

// R-PLN-01 to R-PLN-03. Amounts are minor units (1,500 SAR = 150,000).
public class PlanCalculatorTests
{
    // FY 2026 of the section 3 example: payday 27, Jan 2026 to Dec 2026.
    private static readonly FiscalYear Fy = FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 10, 17), 1, 27);

    private static Dictionary<string, long> EveryMonth(long amount) =>
        Fy.Periods.ToDictionary(period => period.Key, _ => amount);

    // Groceries (Food) 1,500 every month, Fuel (Transport) 400 from Sep.
    private static readonly PlanAmounts Groceries = new("groceries", "food", EveryMonth(150_000));
    private static readonly PlanAmounts Fuel = new("fuel", "transport", new Dictionary<string, long>
    {
        ["2026-09"] = 40_000, ["2026-10"] = 40_000, ["2026-11"] = 40_000, ["2026-12"] = 40_000,
    });

    // Salary 15,000 and rent income 2,000 every month, a once bonus of 500.25 in Sep.
    private static readonly IncomeItem[] Incomes =
    [
        new("s", IncomeKind.Salary, "Salary", 1_500_000, "2026-01", null),
        new("r", IncomeKind.Additional, "Rent", 200_000, "2026-01", null),
        new("b", IncomeKind.Additional, "Bonus", 50_025, "2026-09", "2026-09"),
    ];

    [Fact]
    public void A_missing_amount_counts_as_zero()
    {
        Assert.Equal(0, PlanCalculator.AmountFor(Fuel, "2026-01"));
        Assert.Equal(40_000, PlanCalculator.AmountFor(Fuel, "2026-09"));
    }

    [Fact]
    public void Month_planned_is_the_sum_of_all_sub_categories()
    {
        Assert.Equal(150_000, PlanCalculator.MonthPlanned([Groceries, Fuel], "2026-01"));
        Assert.Equal(190_000, PlanCalculator.MonthPlanned([Groceries, Fuel], "2026-09"));
        Assert.Equal(0, PlanCalculator.MonthPlanned([], "2026-09"));
    }

    [Fact]
    public void Category_planned_is_the_sum_of_its_sub_categories()
    {
        var dining = new PlanAmounts("dining", "food", new Dictionary<string, long> { ["2026-09"] = 30_000 });

        Assert.Equal(180_000, PlanCalculator.CategoryPlanned([Groceries, Fuel, dining], "food", "2026-09"));
        Assert.Equal(40_000, PlanCalculator.CategoryPlanned([Groceries, Fuel, dining], "transport", "2026-09"));
        Assert.Equal(0, PlanCalculator.CategoryPlanned([Groceries, Fuel, dining], "transport", "2026-08"));
    }

    [Fact]
    public void Total_counts_only_the_fiscal_years_months()
    {
        var withOtherFy = new PlanAmounts("x", "food", new Dictionary<string, long> { ["2026-12"] = 100, ["2027-01"] = 999 });

        Assert.Equal(1_800_000, PlanCalculator.Total(Groceries, Fy.Periods));
        Assert.Equal(160_000, PlanCalculator.Total(Fuel, Fy.Periods));
        Assert.Equal(100, PlanCalculator.Total(withOtherFy, Fy.Periods));
    }

    // R-PLN-03: green when 0 or more, red when negative.
    [Theory]
    [InlineData(1_000_000, 400_000, 600_000)]
    [InlineData(400_000, 400_000, 0)]
    [InlineData(0, 150_000, -150_000)]
    public void Unplanned_is_income_minus_planned(long income, long planned, long expected)
    {
        Assert.Equal(expected, PlanCalculator.Unplanned(income, planned));
    }

    // The hand check behind the M4 "Done when": Jan and Sep of the example above.
    [Fact]
    public void Worked_example_totals_row()
    {
        var plans = new[] { Groceries, Fuel };
        var income = IncomeCalculator.TotalsForPeriods(Incomes, Fy.Periods);

        // Jan: planned 1,500; income 15,000 + 2,000 = 17,000; unplanned 15,500.
        Assert.Equal(150_000, PlanCalculator.MonthPlanned(plans, "2026-01"));
        Assert.Equal(1_700_000, income["2026-01"]);
        Assert.Equal(1_550_000, PlanCalculator.Unplanned(income["2026-01"], PlanCalculator.MonthPlanned(plans, "2026-01")));

        // Sep: planned 1,500 + 400 = 1,900; income 17,000 + 500.25 = 17,500.25; unplanned 15,600.25.
        Assert.Equal(190_000, PlanCalculator.MonthPlanned(plans, "2026-09"));
        Assert.Equal(1_750_025, income["2026-09"]);
        Assert.Equal(1_560_025, PlanCalculator.Unplanned(income["2026-09"], PlanCalculator.MonthPlanned(plans, "2026-09")));
    }

    // Owner's choice (M4): 0 clears a month.
    [Fact]
    public void Normalize_drops_zero_amounts()
    {
        var normalized = PlanCalculator.NormalizeAmounts(
            new Dictionary<string, long> { ["2026-01"] = 150_000, ["2026-02"] = 0 }, Fy.Periods);

        Assert.Equal(new Dictionary<string, long> { ["2026-01"] = 150_000 }, normalized);
    }

    // R-CAL-09: only the FY's stored months.
    [Theory]
    [InlineData("2027-01")]
    [InlineData("2025-12")]
    [InlineData("2026-1")]
    public void Normalize_refuses_a_key_outside_the_fiscal_year(string key)
    {
        Assert.Null(PlanCalculator.NormalizeAmounts(new Dictionary<string, long> { [key] = 100 }, Fy.Periods));
    }

    // ENT-01: remaining can go below 0 when over plan.
    [Theory]
    [InlineData(150_000, 119_999, 30_001)]
    [InlineData(150_000, 150_000, 0)]
    [InlineData(150_000, 160_000, -10_000)]
    [InlineData(0, 10_000, -10_000)]
    public void Remaining_is_planned_minus_spent(long planned, long spent, long remaining)
    {
        Assert.Equal(remaining, PlanCalculator.Remaining(planned, spent));
    }

    // DSH-02: saved is income minus spent, and goes below 0 when more is spent than earned.
    [Theory]
    [InlineData(15_350_025, 134_999, 15_215_026)]
    [InlineData(100_000, 100_000, 0)]
    [InlineData(15_350_025, 20_134_999, -4_784_974)]
    [InlineData(0, 0, 0)]
    public void Saved_is_income_minus_spent(long income, long spent, long saved)
    {
        Assert.Equal(saved, PlanCalculator.Saved(income, spent));
    }

    // ENT-01: rounded down, so 79.99% stays below the 80% "Near limit" line.
    [Theory]
    [InlineData(150_000, 0, 0)]
    [InlineData(150_000, 119_999, 79)]
    [InlineData(150_000, 120_000, 80)]
    [InlineData(150_000, 150_000, 100)]
    [InlineData(150_000, 150_001, 100)]
    [InlineData(150_000, 300_000, 200)]
    public void Percent_is_rounded_down(long planned, long spent, int percent)
    {
        Assert.Equal(percent, PlanCalculator.Percent(planned, spent));
    }

    // "No plan for this month".
    [Fact]
    public void Percent_is_null_without_a_plan()
    {
        Assert.Null(PlanCalculator.Percent(0, 10_000));
    }

    // FY-02 (owner's choice, M8): the same month a year earlier, from whichever FY holds it.
    // R-CAL-08 Example A: FY 2026 → transition FY 2027 (Jan to Jun 2027) → FY 2027/28 (Jul 2027 to Jun 2028).
    [Fact]
    public void Copy_example_A_fills_every_month_from_the_same_month_a_year_earlier()
    {
        var active = new HashSet<string> { "groceries", "fuel" };
        var transition = FiscalCalendar.NextFiscalYear(Fy, fiscalStartMonth: 7, payday: 27);

        var toTransition = PlanCalculator.CopyForNextFiscalYear([Groceries, Fuel], transition.Periods, active);

        // Fuel has nothing in Jan to Jun 2026, so it gets no plan.
        var groceries = Assert.Single(toTransition);
        Assert.Equal("groceries", groceries.SubCategoryId);
        Assert.Equal("food", groceries.CategoryId);
        Assert.Equal(
            new[] { "2027-01", "2027-02", "2027-03", "2027-04", "2027-05", "2027-06" },
            groceries.Amounts.Keys.Order());
        Assert.All(groceries.Amounts.Values, amount => Assert.Equal(150_000, amount));

        var fy2027 = FiscalCalendar.NextFiscalYear(transition, fiscalStartMonth: 7, payday: 27);

        var toFy2027 = PlanCalculator.CopyForNextFiscalYear([Groceries, Fuel, groceries], fy2027.Periods, active);

        // Jul to Dec 2027 come from FY 2026, Jan to Jun 2028 from the transition FY.
        var copiedGroceries = toFy2027.Single(plan => plan.SubCategoryId == "groceries");
        Assert.Equal(fy2027.Periods.Select(period => period.Key), copiedGroceries.Amounts.Keys.Order());
        Assert.All(copiedGroceries.Amounts.Values, amount => Assert.Equal(150_000, amount));
        var copiedFuel = toFy2027.Single(plan => plan.SubCategoryId == "fuel");
        Assert.Equal(
            new Dictionary<string, long> { ["2027-09"] = 40_000, ["2027-10"] = 40_000, ["2027-11"] = 40_000, ["2027-12"] = 40_000 },
            copiedFuel.Amounts);
    }

    // R-CAL-08 Example B: the payday changes, but the keys stay YYYY-MM.
    [Fact]
    public void Copy_example_B_keeps_the_month_keys()
    {
        var next = FiscalCalendar.NextFiscalYear(Fy, fiscalStartMonth: 1, payday: 1);

        var copied = PlanCalculator.CopyForNextFiscalYear([Fuel], next.Periods, new HashSet<string> { "fuel" });

        Assert.Equal(
            new Dictionary<string, long> { ["2027-09"] = 40_000, ["2027-10"] = 40_000, ["2027-11"] = 40_000, ["2027-12"] = 40_000 },
            Assert.Single(copied).Amounts);
    }

    [Fact]
    public void Copy_skips_archived_sub_categories_and_empty_plans()
    {
        var next = FiscalCalendar.NextFiscalYear(Fy, fiscalStartMonth: 1, payday: 27);
        var empty = new PlanAmounts("empty", "food", new Dictionary<string, long>());

        var copied = PlanCalculator.CopyForNextFiscalYear([Groceries, Fuel, empty], next.Periods, new HashSet<string> { "fuel", "empty" });

        Assert.Equal("fuel", Assert.Single(copied).SubCategoryId);
    }
}
