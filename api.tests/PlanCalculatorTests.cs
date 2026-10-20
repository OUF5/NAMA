using Api.Domain;

namespace Api.Tests;

// R-PLN-01 to R-PLN-05. Amounts are minor units (1,500 SAR = 150,000).
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

    private static PlanAmounts Plan(params (string Key, long Amount)[] amounts) =>
        new("trip", "travel", amounts.ToDictionary(item => item.Key, item => item.Amount));

    // R-PLN-03 (changed 20 Oct 2026), the owner's example: income 17,000 a month; planned 15,000, 16,000,
    // 19,000 (a trip) and 15,000. Month by month the trip month was -2,000 (red); carried, it is 1,000:
    // January's and February's surplus pays for it.
    [Fact]
    public void Unassigned_carries_from_month_to_month()
    {
        var periods = Fy.Periods.Take(4).ToList();
        var income = periods.ToDictionary(period => period.Key, _ => 1_700_000L);
        var plan = Plan(("2026-01", 1_500_000), ("2026-02", 1_600_000), ("2026-03", 1_900_000), ("2026-04", 1_500_000));

        var months = PlanCalculator.RunningUnassigned(periods, income, [plan]);

        Assert.Equal(["2026-01", "2026-02", "2026-03", "2026-04"], months.Select(month => month.PeriodKey));
        Assert.Equal([0L, 200_000, 300_000, 100_000], months.Select(month => month.CarriedIn));
        Assert.Equal([200_000L, 300_000, 100_000, 300_000], months.Select(month => month.Unassigned));
        Assert.Equal(300_000, PlanCalculator.PlannedSaved(months));
    }

    // R-PLN-03: more planned than received so far stays negative until later income covers it.
    [Fact]
    public void A_negative_unassigned_carries_too()
    {
        var periods = Fy.Periods.Take(3).ToList();
        var income = periods.ToDictionary(period => period.Key, _ => 100_000L);
        var plan = Plan(("2026-01", 250_000));

        var months = PlanCalculator.RunningUnassigned(periods, income, [plan]);

        Assert.Equal([-150_000L, -50_000, 50_000], months.Select(month => month.Unassigned));
    }

    // The hand check behind the M4 "Done when": Jan and Sep of the example above, now carried.
    [Fact]
    public void Worked_example_totals_row()
    {
        var income = IncomeCalculator.TotalsForPeriods(Incomes, Fy.Periods);

        var months = PlanCalculator.RunningUnassigned(Fy.Periods, income, [Groceries, Fuel]);

        // Jan: planned 1,500; income 15,000 + 2,000 = 17,000; unassigned 15,500.
        Assert.Equal(new UnassignedMonth("2026-01", 0, 1_700_000, 150_000), months[0]);
        Assert.Equal(1_550_000, months[0].Unassigned);
        // Sep: 8 months of 15,500 carried (124,000); income 17,000 + 500.25; planned 1,500 + 400;
        // unassigned 124,000 + 17,500.25 - 1,900 = 139,600.25.
        Assert.Equal(new UnassignedMonth("2026-09", 12_400_000, 1_750_025, 190_000), months[8]);
        Assert.Equal(13_960_025, months[8].Unassigned);
        // R-PLN-04: FY income 204,500.25 - FY planned 19,600.
        Assert.Equal(18_490_025, PlanCalculator.PlannedSaved(months));
    }

    [Fact]
    public void Planned_saved_of_no_months_is_zero()
    {
        Assert.Equal(0, PlanCalculator.PlannedSaved([]));
    }

    // R-PLN-06: the first month that carries contains the day NAMA was set up; never before the FY.
    [Theory]
    [InlineData(null, 0)]
    [InlineData("2025-12-01", 0)]
    [InlineData("2026-01-27", 0)]
    [InlineData("2026-02-26", 0)]
    [InlineData("2026-02-27", 1)]
    [InlineData("2026-10-17", 8)]
    [InlineData("2027-01-26", 11)]
    [InlineData("2027-02-01", 12)]
    public void Carry_starts_in_the_month_NAMA_was_set_up(string? startedOn, int expected)
    {
        DateOnly? day = startedOn is null ? null : DateOnly.ParseExact(startedOn, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, PlanCalculator.CarryStart(Fy.Periods, day));
    }

    // R-PLN-06, the owner's check on 20 Oct 2026: Rent 1,000 every month from Aug, set up in Oct. Aug and
    // Sep were never tracked, so their 1,000 each does not carry: Oct has 1,000, not 3,000.
    [Fact]
    public void Months_before_the_start_stand_alone()
    {
        var months = PlanCalculator.Carry([(100_000, 0), (100_000, 0), (100_000, 0), (100_000, 40_000)], carryStart: 2);

        Assert.Equal([0L, 0, 0, 100_000], months.Select(month => month.CarriedIn));
        Assert.Equal([100_000L, 100_000, 100_000, 160_000], months.Select(month => month.Remaining));

        var periods = Fy.Periods.Take(4).ToList();
        var unassigned = PlanCalculator.RunningUnassigned(periods, periods.ToDictionary(period => period.Key, _ => 1_000_000L),
            [Plan(("2026-01", 100_000), ("2026-02", 100_000), ("2026-03", 100_000), ("2026-04", 100_000))], carryStart: 2);
        Assert.Equal([0L, 0, 0, 900_000], unassigned.Select(month => month.CarriedIn));
        Assert.Equal(1_800_000, PlanCalculator.PlannedSaved(unassigned));
    }

    // R-PLN-05, the owner's example: Groceries 1,500 a month; spent 1,200, 1,700 and 1,500. Month by
    // month Feb was -200 (red); carried, January's 300 covers it.
    [Fact]
    public void Remaining_carries_from_month_to_month()
    {
        var months = PlanCalculator.Carry([(150_000, 120_000), (150_000, 170_000), (150_000, 150_000)]);

        Assert.Equal([0L, 30_000, 10_000], months.Select(month => month.CarriedIn));
        Assert.Equal([150_000L, 180_000, 160_000], months.Select(month => month.Available));
        Assert.Equal([30_000L, 10_000, 10_000], months.Select(month => month.Remaining));
    }

    // R-PLN-05: an overspend carries as it is and lowers the next months until a plan covers it.
    [Fact]
    public void An_overspend_carries_as_it_is()
    {
        var months = PlanCalculator.Carry([(0, 20_000), (0, 0), (50_000, 10_000)]);

        Assert.Equal([-20_000L, -20_000, 20_000], months.Select(month => month.Remaining));
        Assert.Equal(-20_000, months[1].Available);
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

    // ENT-01: remaining (available - spent) can go below 0 when over plan.
    [Theory]
    [InlineData(150_000, 119_999, 30_001)]
    [InlineData(150_000, 150_000, 0)]
    [InlineData(150_000, 160_000, -10_000)]
    [InlineData(0, 10_000, -10_000)]
    public void Remaining_is_available_minus_spent(long available, long spent, long remaining)
    {
        Assert.Equal(remaining, PlanCalculator.Remaining(available, spent));
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
    public void Percent_is_rounded_down(long available, long spent, int percent)
    {
        Assert.Equal(percent, PlanCalculator.Percent(available, spent));
    }

    // "No plan for this month", and a month that carried in more overspending than it planned.
    [Fact]
    public void Percent_is_null_when_nothing_is_available()
    {
        Assert.Null(PlanCalculator.Percent(0, 10_000));
        Assert.Null(PlanCalculator.Percent(-5_000, 0));
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
