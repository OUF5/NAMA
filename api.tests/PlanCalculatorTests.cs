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
}
