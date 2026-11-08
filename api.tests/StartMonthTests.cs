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

    // Owner note 32: Tracker's rows and months (the Year view's Available) carry from the FY's first
    // month, Aug, whatever month NAMA was set up in: Rent's unspent 1,000 a month adds up. The
    // projection's still to spend keeps R-PLN-06: Oct starts from 0, so 1,000 is left to spend.
    [Fact]
    public async Task Tracker_months_and_cells_carry_from_the_fiscal_years_first_month()
    {
        var overview = await Get(await SetUp(), $"/api/fiscal-years/{Fy}/overview");

        Assert.Equal("2026-10", overview["carryFromPeriodKey"]!.GetValue<string>());
        var months = overview["months"]!.AsArray();
        Assert.Equal([0m, 1_000m, 2_000m, 3_000m], months.Take(4).Select(month => Dec(month!["carriedIn"])));
        Assert.Equal([1_000m, 2_000m, 3_000m, 4_000m], months.Take(4).Select(month => Dec(month!["remaining"])));
        var rentOct = overview["rows"]![0]!["subCategories"]![0]!["months"]![2]!;
        Assert.Equal((2_000m, 1_000m, 3_000m), (Dec(rentOct["carriedIn"]), Dec(rentOct["planned"]), Dec(rentOct["remaining"])));
        Assert.Equal(1_000m, Dec(overview["planVsActual"]!["stillToSpend"]));
        // DSH-02 from Oct: one started month, 10,000 income, nothing spent; the plan meant to keep 9,000.
        var toDate = overview["toDate"]!;
        Assert.Equal((10_000m, 0m, 10_000m, 9_000m),
            (Dec(toDate["income"]), Dec(toDate["spent"]), Dec(toDate["saved"]), Dec(toDate["plannedSaved"])));
    }

    // R-PLN-09, the owner's case (owner note 32): 1,500 of Rent's 1,000 was spent in Aug and in Sep,
    // entered after NAMA was set up in Oct. Those overspends count, though Aug and Sep stand alone for
    // the carry (R-PLN-06). Oct counted over the last month (Sep, the default) has 500 to make up, and
    // 1,000 left of its plan; over the last 2 months (since Aug), 1,000 and nothing after covering. Aug,
    // the FY's first month, has no months back. The same with Tracker filtered to Rent.
    [Fact]
    public async Task Overspent_to_make_up_counts_the_months_back_whenever_NAMA_was_set_up()
    {
        var sub = await SetUp();
        await factory.Store.AddEntryAsync(sub, new Entry("e1", new DateOnly(2026, 8, 5), "2026-08", Fy, "food", "rent", "rajhi", 150_000, ""));
        await factory.Store.AddEntryAsync(sub, new Entry("e2", new DateOnly(2026, 9, 5), "2026-09", Fy, "food", "rent", "rajhi", 150_000, ""));
        async Task<JsonNode> Overspent(string periodKey, string query) =>
            (await Get(sub, $"/api/fiscal-years/{Fy}/months/{periodKey}{query}"))["overspent"]!;
        static (int, string?, decimal) Head(JsonNode node) =>
            (node["monthsBack"]!.GetValue<int>(), node["from"]?["key"]?.GetValue<string>(), Dec(node["total"]));
        static (string, decimal, decimal, decimal) Row(JsonNode node) =>
            (node["categoryId"]!.GetValue<string>(), Dec(node["overspent"]), Dec(node["left"]), Dec(node["after"]));

        foreach (var filter in new[] { "?", "?categoryId=food&subCategoryId=rent&" })
        {
            var lastMonth = await Overspent("2026-10", filter);
            Assert.Equal((1, "2026-09", 500m), Head(lastMonth));
            Assert.Equal(("food", 500m, 1_000m, 500m), Row(lastMonth["categories"]![0]!));

            var twoMonths = await Overspent("2026-10", $"{filter}back=2");
            Assert.Equal((2, "2026-08", 1_000m), Head(twoMonths));
            Assert.Equal(("food", 1_000m, 1_000m, 0m), Row(twoMonths["categories"]![0]!));

            // At most the FY's months before Oct.
            Assert.Equal((2, "2026-08", 1_000m), Head(await Overspent("2026-10", $"{filter}back=6")));

            var first = await Overspent("2026-08", $"{filter}back=3");
            Assert.Equal((0, null, 0m), Head(first));
            Assert.Empty(first["categories"]!.AsArray());
        }

        // 600 spent in Oct leaves 400 of its plan: 600 short of the 1,000 to make up.
        await factory.Store.AddEntryAsync(sub, new Entry("e3", new DateOnly(2026, 10, 5), "2026-10", Fy, "food", "rent", "rajhi", 60_000, ""));
        Assert.Equal(("food", 1_000m, 400m, -600m), Row((await Overspent("2026-10", "?back=2"))["categories"]![0]!));
    }

    // Owner note 32, the owner's case: 1,500 of Rent's 1,000 in Aug and in Sep, entered after the setup
    // in Oct, carry into the Year view: Oct has 1,000 - 1,000 = 0 available.
    [Fact]
    public async Task An_overspend_before_the_setup_carries_into_the_year_view()
    {
        var sub = await SetUp();
        await factory.Store.AddEntryAsync(sub, new Entry("e1", new DateOnly(2026, 8, 5), "2026-08", Fy, "food", "rent", "rajhi", 150_000, ""));
        await factory.Store.AddEntryAsync(sub, new Entry("e2", new DateOnly(2026, 9, 5), "2026-09", Fy, "food", "rent", "rajhi", 150_000, ""));

        var rent = (await Get(sub, $"/api/fiscal-years/{Fy}/overview"))["rows"]![0]!["subCategories"]![0]!["months"]!.AsArray();

        Assert.Equal([0m, -500m, -1_000m, 0m], rent.Take(4).Select(cell => Dec(cell!["carriedIn"])));
        Assert.Equal(0m, Dec(rent[2]!["carriedIn"]) + Dec(rent[2]!["planned"]));
    }

    // Tracker's Totals (owner note 18): the owner's choice is that months before NAMA was set up count
    // too, with what was entered for them, so Accumulated savings adds up from Aug: 10,000 a month, and
    // 9,250 in Sep after a back-filled 750. FY to date (Dashboard) and the balance still start in Oct.
    [Fact]
    public async Task Tracker_totals_add_up_unspent_from_the_fiscal_years_first_month()
    {
        var sub = await SetUp();
        await factory.Store.AddEntryAsync(sub, new Entry("e1", new DateOnly(2026, 9, 5), "2026-09", Fy, "food", "rent", "rajhi", 75_000, ""));

        var overview = await Get(sub, $"/api/fiscal-years/{Fy}/overview");

        var months = overview["months"]!.AsArray();
        Assert.Equal([10_000m, 9_250m, 10_000m], months.Take(3).Select(month => Dec(month!["unspent"])));
        Assert.Equal([10_000m, 19_250m, 29_250m], months.Take(3).Select(month => Dec(month!["saved"])));
        Assert.Null(months[3]!["saved"]);
        Assert.Equal(29_250m, Dec(overview["totals"]!["saved"]));
        Assert.Equal(10_000m, Dec(overview["toDate"]!["saved"]));
        var summary = await Get(sub, "/api/summary");
        Assert.Equal(2_500m, Dec(summary["balance"]));
    }

    // Plan (owner note 10): Accumulated Planned Savings is the plan alone, so it adds up from Aug, the
    // FY's first month, even though NAMA was set up in Oct: 9,000 a month, 10,000 in Feb (the bonus).
    // The FY column is the whole FY: income 12 × 10,000 + 1,000 = 121,000, planned 12 × 1,000 = 12,000,
    // Planned saved 109,000.
    [Fact]
    public async Task Plan_adds_up_planned_savings_from_the_fiscal_years_first_month()
    {
        var plans = await Get(await SetUp(), $"/api/fiscal-years/{Fy}/plans");

        Assert.Null(plans["carryFromPeriodKey"]);
        var totals = plans["totals"]!.AsArray();
        Assert.Equal([9_000m, 18_000m, 27_000m, 36_000m], totals.Take(4).Select(month => Dec(month!["unassigned"])));
        Assert.All(totals, month => Assert.Equal(month!["income"]!.GetValue<decimal>() == 11_000m ? 10_000m : 9_000m, Dec(month["thisMonth"])));
        Assert.Equal(109_000m, Dec(totals[^1]!["unassigned"]));
        var fy = plans["fiscalYearTotals"]!;
        Assert.Equal((121_000m, 12_000m, 109_000m), (Dec(fy["income"]), Dec(fy["planned"]), Dec(fy["plannedSaved"])));
    }

    // Owner note 24: Dashboard's Plan vs actual and Saving cards count from Aug, the FY's first month,
    // like Tracker and Plan. Aug to Oct planned 3 × 10,000 income and 3 × 1,000 spending; 750 was spent
    // in Sep. Plan meant to keep 27,000 by Oct; 29,250 is kept so far, with Oct's 1,000 still to spend.
    // The months to come add 9 × 9,000 + the 1,000 bonus = 82,000, so the FY ends at 110,250, 1,250
    // above Plan's 109,000 (Aug kept 1,000 and Sep 250 more than planned).
    [Fact]
    public async Task Dashboard_plan_vs_actual_counts_from_the_fiscal_years_first_month()
    {
        var sub = await SetUp();
        await factory.Store.AddEntryAsync(sub, new Entry("e1", new DateOnly(2026, 9, 5), "2026-09", Fy, "food", "rent", "rajhi", 75_000, ""));

        var vs = (await Get(sub, $"/api/fiscal-years/{Fy}/overview"))["planVsActual"]!;

        Assert.Equal((30_000m, 30_000m, 0m), (Dec(vs["plannedIncome"]), Dec(vs["income"]), Dec(vs["additionalIncome"])));
        Assert.Equal((3_000m, 750m, 1_000m), (Dec(vs["planned"]), Dec(vs["spent"]), Dec(vs["stillToSpend"])));
        Assert.Equal((27_000m, 29_250m), (Dec(vs["plannedSaved"]), Dec(vs["saved"])));
        Assert.Equal((82_000m, 109_000m, 110_250m), (Dec(vs["futureUnspent"]), Dec(vs["plannedSavedAtEnd"]), Dec(vs["projectedSaved"])));
    }

    // The totals' Planned saved (R-PLN-04) still counts from Oct (R-PLN-06): Oct to Jul, 91,000.
    // Dashboard no longer shows it (owner note 24).
    [Fact]
    public async Task Dashboard_planned_saved_still_counts_from_October()
    {
        var overview = await Get(await SetUp(), $"/api/fiscal-years/{Fy}/overview");

        Assert.Equal(91_000m, Dec(overview["totals"]!["plannedSaved"]));
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
        // Owner note 11: the projection says it counts from Oct, so Dashboard can name it.
        var projection = summary["projection"]!;
        Assert.Equal(("2026-10", "Oct 2026"),
            (projection["countedFrom"]!["key"]!.GetValue<string>(), projection["countedFrom"]!["label"]!.GetValue<string>()));
        Assert.Equal(91_000m, Dec(projection["plannedSaved"]));
    }

    // Owner note 32: Dashboard's current month (from the summary): Sep's 500 over on Rent, entered after
    // the setup in Oct, is last month's overspend; the 300 Groceries left in Sep does not offset it,
    // because each sub-category counts on its own (R-PLN-09).
    [Fact]
    public async Task The_summary_says_last_months_overspend_by_sub_category()
    {
        var sub = await SetUp();
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 1));
        await factory.Store.SetPlanAsync(sub, Fy, new PlanAmounts("groceries", "food", new Dictionary<string, long> { ["2026-09"] = 30_000 }));
        await factory.Store.AddEntryAsync(sub, new Entry("e1", new DateOnly(2026, 9, 5), "2026-09", Fy, "food", "rent", "rajhi", 150_000, ""));

        var month = (await Get(sub, "/api/summary"))["month"]!;

        Assert.Equal(("2026-10", 500m), (month["period"]!["key"]!.GetValue<string>(), Dec(month["overspentLastMonth"])));
    }

    // Owner note 32: Add's indicator shows October on its own, and Sep's overspend (1,500 of Rent's
    // 1,000, entered after NAMA was set up in Oct) beside it (R-PLN-09, not R-PLN-06).
    [Fact]
    public async Task The_add_form_indicator_shows_october_alone_and_septembers_overspend()
    {
        var sub = await SetUp();
        await factory.Store.AddEntryAsync(sub, new Entry("e1", new DateOnly(2026, 9, 5), "2026-09", Fy, "food", "rent", "rajhi", 150_000, ""));

        var indicator = await Get(sub, "/api/indicator?subCategoryId=rent&date=2026-10-20");

        Assert.Equal(
            (1_000m, 0m, 1_000m, 500m),
            (Dec(indicator["planned"]), Dec(indicator["spent"]), Dec(indicator["remaining"]), Dec(indicator["overspentLastMonth"])));
    }
}
