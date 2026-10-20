using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// R-PLN-06, the owner's check on 20 Oct 2026: FY 2026/27 runs Aug 2026 to Jul 2027 with payday 1; the
// salary is 10,000 from Aug, a bonus of 1,000 comes in Feb 2027, and Rent is planned at 1,000 every
// month. NAMA was set up on 20 Oct 2026 with Alrajhi 1,500 and SNB 1,000. Aug and Sep were never
// tracked, so nothing carries out of them: Oct has 1,000 left, not 3,000.
public class StartMonthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Fy = "2026-08";
    private static readonly DateTimeOffset Today = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly SetUpOn = new(2026, 10, 20);
    private static readonly FiscalYear FiscalYear = FiscalCalendar.FirstFiscalYear(SetUpOn, 8, 1);

    private async Task<string> SetUp()
    {
        factory.Time.UtcNow = Today;
        var sub = await SetUpUser(factory,
            incomes: [
                new("s", IncomeKind.Salary, "Salary", 1_000_000, Fy, null),
                new("b", IncomeKind.Additional, "Bonus", 100_000, "2027-02", "2027-02"),
            ],
            fiscalYear: FiscalYear,
            accounts: [
                new BankAccount("rajhi", "Alrajhi", Opening: new OpeningBalance(150_000, SetUpOn)),
                new BankAccount("snb", "SNB", Opening: new OpeningBalance(100_000, SetUpOn)),
            ],
            startedOn: SetUpOn,
            paydayDay: 1);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("rent", "food", "Rent", 0));
        await factory.Store.SetPlanAsync(sub, Fy, new PlanAmounts("rent", "food", FiscalYear.Periods.ToDictionary(period => period.Key, _ => 100_000L)));
        return sub;
    }

    private async Task<JsonNode> Get(string sub, string path)
    {
        factory.Time.UtcNow = Today;
        var response = await Send(factory, HttpMethod.Get, path, sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static decimal Dec(JsonNode? node) => node!.GetValue<decimal>();

    [Fact]
    public void The_fiscal_year_is_the_owners()
    {
        Assert.Equal((Fy, new DateOnly(2026, 8, 1), new DateOnly(2027, 7, 31)), (FiscalYear.Id, FiscalYear.StartDate, FiscalYear.EndDate));
    }

    // Tracker: Aug and Sep stand alone; Oct starts from 0; what Oct leaves carries into Nov.
    [Fact]
    public async Task Tracker_months_and_cells_carry_from_October()
    {
        var overview = await Get(await SetUp(), $"/api/fiscal-years/{Fy}/overview");

        Assert.Equal("2026-10", overview["carryFromPeriodKey"]!.GetValue<string>());
        var months = overview["months"]!.AsArray();
        Assert.Equal([0m, 0m, 0m, 1_000m], months.Take(4).Select(month => Dec(month!["carriedIn"])));
        Assert.Equal([1_000m, 1_000m, 1_000m, 2_000m], months.Take(4).Select(month => Dec(month!["remaining"])));
        var rentOct = overview["rows"]![0]!["subCategories"]![0]!["months"]![2]!;
        Assert.Equal((0m, 1_000m, 1_000m), (Dec(rentOct["carriedIn"]), Dec(rentOct["planned"]), Dec(rentOct["remaining"])));
        // DSH-02 from Oct: one started month, 10,000 income, nothing spent; the plan meant to keep 9,000.
        var toDate = overview["toDate"]!;
        Assert.Equal((10_000m, 0m, 10_000m, 9_000m),
            (Dec(toDate["income"]), Dec(toDate["spent"]), Dec(toDate["saved"]), Dec(toDate["plannedSaved"])));
    }

    // Plan: Unassigned carries from Oct (9,000), and the FY column covers Oct to Jul so it adds up:
    // income 10 × 10,000 + 1,000 = 101,000, planned 10 × 1,000 = 10,000, Planned saved 91,000.
    [Fact]
    public async Task Plan_carries_unassigned_from_October()
    {
        var plans = await Get(await SetUp(), $"/api/fiscal-years/{Fy}/plans");

        Assert.Equal("2026-10", plans["carryFromPeriodKey"]!.GetValue<string>());
        var totals = plans["totals"]!.AsArray();
        Assert.Equal([0m, 0m, 0m, 9_000m], totals.Take(4).Select(month => Dec(month!["carriedIn"])));
        Assert.Equal([9_000m, 9_000m, 9_000m, 18_000m], totals.Take(4).Select(month => Dec(month!["unassigned"])));
        Assert.All(totals, month => Assert.Equal(month!["planned"]!.GetValue<decimal>() == 1_000m && month["income"]!.GetValue<decimal>() == 11_000m ? 10_000m : 9_000m, Dec(month["thisMonth"])));
        Assert.Equal(91_000m, Dec(totals[^1]!["unassigned"]));
        var fy = plans["fiscalYearTotals"]!;
        Assert.Equal((101_000m, 10_000m, 91_000m), (Dec(fy["income"]), Dec(fy["planned"]), Dec(fy["plannedSaved"])));
    }

    // The top bar: 1,000 left in Oct, never more than what the plan gives; the balance is 2,500.
    [Fact]
    public async Task The_top_bar_shows_october_alone()
    {
        var summary = await Get(await SetUp(), "/api/summary");

        var month = summary["month"]!;
        Assert.Equal((0m, 1_000m, 1_000m, 9_000m),
            (Dec(month["carriedIn"]), Dec(month["planned"]), Dec(month["remaining"]), Dec(month["unassigned"])));
        Assert.Equal(2_500m, Dec(summary["balance"]));
    }

    [Fact]
    public async Task The_add_form_indicator_shows_october_alone()
    {
        var indicator = await Get(await SetUp(), "/api/indicator?subCategoryId=rent&date=2026-10-20");

        Assert.Equal((0m, 1_000m, 1_000m), (Dec(indicator["carriedIn"]), Dec(indicator["available"]), Dec(indicator["remaining"])));
    }
}
