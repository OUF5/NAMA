using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.6 Months and 7.5 GET /api/fiscal-years/{fyId}/overview (MON-01, MON-02, R-STA-01,
// R-PLN-02, R-INC-01). FY 2026 runs Jan to Dec 2026 with payday 27; today is set per call in
// Riyadh (UTC+3, R-CAL-10).
public class OverviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Fy = "2026-01";

    // The M4 hand check: salary 15,000 and rent income 2,000 every month, a once bonus of 500.25 in Sep.
    private static readonly IncomeItem[] Incomes =
    [
        new("s", IncomeKind.Salary, "Salary", 1_500_000, "2026-01", null),
        new("r", IncomeKind.Additional, "Rent", 200_000, "2026-01", null),
        new("b", IncomeKind.Additional, "Bonus", 50_025, "2026-09", "2026-09"),
    ];

    private static readonly string[] Months2026 =
        ["2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-11", "2026-12"];

    // Groceries 1,500 every month and Fuel 400 from Sep (M4). Entries as in M5: Groceries 400 and
    // 799.99 in Sep, 50 in Aug; Fuel 100 in Sep.
    private async Task<string> SetUp()
    {
        var sub = await SetUpUser(factory, Incomes);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("fuel", "transport", "Fuel", 0));
        await factory.Store.SetPlanAsync(sub, Fy, new PlanAmounts("groceries", "food", Months2026.ToDictionary(key => key, _ => 150_000L)));
        await factory.Store.SetPlanAsync(sub, Fy, new PlanAmounts("fuel", "transport",
            new Dictionary<string, long> { ["2026-09"] = 40_000, ["2026-10"] = 40_000, ["2026-11"] = 40_000, ["2026-12"] = 40_000 }));
        await AddEntry(sub, "groceries", "2026-10-01", 400m);
        await AddEntry(sub, "groceries", "2026-10-17", 799.99m);
        await AddEntry(sub, "groceries", "2026-09-10", 50m);
        await AddEntry(sub, "fuel", "2026-10-01", 100m);
        return sub;
    }

    private async Task AddEntry(string sub, string subCategoryId, string date, decimal amount)
    {
        var response = await Send(factory, HttpMethod.Post, "/api/entries", sub, new JsonObject
        {
            ["subCategoryId"] = subCategoryId,
            ["bankAccountId"] = "bank",
            ["date"] = date,
            ["amount"] = amount,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task<HttpResponseMessage> Get(string sub, string fyId = Fy, DateTimeOffset? utcNow = null)
    {
        factory.Time.UtcNow = utcNow ?? TestTime.Default;
        return Send(factory, HttpMethod.Get, $"/api/fiscal-years/{fyId}/overview", sub);
    }

    private async Task<JsonNode> GetOk(string sub, string fyId = Fy, DateTimeOffset? utcNow = null)
    {
        var response = await Get(sub, fyId, utcNow);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static decimal Dec(JsonNode? node) => node!.GetValue<decimal>();

    private static JsonNode Month(JsonNode overview, string key) =>
        overview["months"]!.AsArray().Single(month => month!["period"]!["key"]!.GetValue<string>() == key)!;

    private static JsonNode Row(JsonNode overview, string categoryId) =>
        overview["rows"]!.AsArray().Single(row => row!["categoryId"]!.GetValue<string>() == categoryId)!;

    private static JsonNode SubRow(JsonNode overview, string categoryId, string subCategoryId) =>
        Row(overview, categoryId)["subCategories"]!.AsArray().Single(row => row!["subCategoryId"]!.GetValue<string>() == subCategoryId)!;

    private static JsonNode Cell(JsonNode row, string periodKey) =>
        row["months"]!.AsArray().Single(cell => cell!["periodKey"]!.GetValue<string>() == periodKey)!;

    private static (decimal, decimal, decimal, decimal) Numbers(JsonNode month) =>
        (Dec(month["income"]), Dec(month["planned"]), Dec(month["spent"]), Dec(month["remaining"]));

    // The hand check: income (R-INC-01), planned (R-PLN-02), spent, and remaining = planned - spent (M7).
    [Fact]
    public async Task Months_and_totals_match_the_hand_check()
    {
        var overview = await GetOk(await SetUp());

        Assert.Equal(Fy, overview["fyId"]!.GetValue<string>());
        Assert.Equal(Months2026, overview["months"]!.AsArray().Select(month => month!["period"]!["key"]!.GetValue<string>()));
        var sep = Month(overview, "2026-09");
        Assert.Equal(("Sep 2026", "2026-09-27", "2026-10-26"), (
            sep["period"]!["label"]!.GetValue<string>(), sep["period"]!["startDate"]!.GetValue<string>(), sep["period"]!["endDate"]!.GetValue<string>()));
        Assert.Equal((17_500.25m, 1_900m, 1_299.99m, 600.01m), Numbers(sep));
        Assert.Equal((17_000m, 1_500m, 50m, 1_450m), Numbers(Month(overview, "2026-08")));
        Assert.Equal((17_000m, 1_500m, 0m, 1_500m), Numbers(Month(overview, "2026-01")));
        Assert.Equal((204_500.25m, 19_600m, 1_349.99m, 18_250.01m), Numbers(overview["totals"]!));
    }

    private static (decimal, decimal, decimal) ToDate(JsonNode overview) =>
        (Dec(overview["toDate"]!["income"]), Dec(overview["toDate"]!["spent"]), Dec(overview["toDate"]!["saved"]));

    // DSH-02 (owner's choice, M9): the months that started on or before today (Jan to Sep on 17 Oct
    // 2026), each with its full income and every entry in it. Income 8 x 17,000 + 17,500.25;
    // spent Aug 50 + Sep 1,299.99; saved = income - spent.
    [Fact]
    public async Task To_date_matches_the_hand_check()
    {
        var overview = await GetOk(await SetUp());

        Assert.Equal((153_500.25m, 1_349.99m, 152_150.26m), ToDate(overview));
    }

    // Every entry of a started month counts, also one dated after today (25 Oct is in Sep 2026).
    [Fact]
    public async Task To_date_counts_entries_dated_later_in_the_current_month()
    {
        var sub = await SetUp();
        await AddEntry(sub, "groceries", "2026-10-25", 100m);
        await AddEntry(sub, "fuel", "2026-10-27", 70m); // Oct 2026 has not started: not counted

        Assert.Equal((153_500.25m, 1_449.99m, 152_050.26m), ToDate(await GetOk(sub)));
    }

    [Fact]
    public async Task Saved_can_be_negative()
    {
        var sub = await SetUp();
        await AddEntry(sub, "groceries", "2026-02-10", 200_000m); // Jan 2026

        Assert.Equal((153_500.25m, 201_349.99m, -47_849.74m), ToDate(await GetOk(sub)));
    }

    // 26 Oct 23:59:59 in Riyadh is still Sep 2026; at 27 Oct 00:00 Oct 2026 starts and its income joins.
    [Theory]
    [InlineData("2026-10-26T20:59:59Z", 153_500.25, 152_150.26)]
    [InlineData("2026-10-26T21:00:00Z", 170_500.25, 169_150.26)]
    public async Task To_date_follows_today_in_the_profile_time_zone(string utcNow, decimal income, decimal saved)
    {
        var overview = await GetOk(await SetUp(), utcNow: DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal((income, 1_349.99m, saved), ToDate(overview));
    }

    [Fact]
    public async Task To_date_is_the_whole_FY_after_it_ends()
    {
        var overview = await GetOk(await SetUp(), utcNow: new DateTimeOffset(2027, 1, 27, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal((204_500.25m, 1_349.99m, 203_150.26m), ToDate(overview));
    }

    [Fact]
    public async Task To_date_is_zero_before_the_FY_starts()
    {
        var overview = await GetOk(await SetUp(), utcNow: new DateTimeOffset(2026, 1, 26, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal((0m, 0m, 0m), ToDate(overview));
    }

    [Fact]
    public async Task Rows_hold_the_planned_and_spent_cells_per_sub_category_and_category()
    {
        var overview = await GetOk(await SetUp());

        Assert.Equal(["food", "transport"], overview["rows"]!.AsArray().Select(row => row!["categoryId"]!.GetValue<string>()));
        var groceries = SubRow(overview, "food", "groceries");
        Assert.Equal((1_500m, 1_199.99m), (Dec(Cell(groceries, "2026-09")["planned"]), Dec(Cell(groceries, "2026-09")["spent"])));
        Assert.Equal((1_500m, 50m), (Dec(Cell(groceries, "2026-08")["planned"]), Dec(Cell(groceries, "2026-08")["spent"])));
        Assert.Equal((18_000m, 1_249.99m), (Dec(groceries["planned"]), Dec(groceries["spent"])));
        var fuel = SubRow(overview, "transport", "fuel");
        Assert.Equal((0m, 0m), (Dec(Cell(fuel, "2026-08")["planned"]), Dec(Cell(fuel, "2026-08")["spent"])));
        Assert.Equal((400m, 100m), (Dec(Cell(fuel, "2026-09")["planned"]), Dec(Cell(fuel, "2026-09")["spent"])));
        Assert.Equal((1_600m, 100m), (Dec(fuel["planned"]), Dec(fuel["spent"])));
        var transport = Row(overview, "transport");
        Assert.Equal(12, transport["months"]!.AsArray().Count);
        Assert.Equal((400m, 100m), (Dec(Cell(transport, "2026-09")["planned"]), Dec(Cell(transport, "2026-09")["spent"])));
        Assert.Equal((1_600m, 100m), (Dec(transport["planned"]), Dec(transport["spent"])));
    }

    // R-STA-01 with today in the profile time zone (R-CAL-10): C = closed, I = in progress, Y = yet to come.
    [Theory]
    [InlineData("2026-10-17T09:00:00Z", "CCCCCCCCIYYY")] // the section 3 example: Sep 2026 is in progress
    [InlineData("2026-10-26T20:59:59Z", "CCCCCCCCIYYY")] // 26 Oct 23:59:59 in Riyadh, last day of Sep 2026
    [InlineData("2026-10-26T21:00:00Z", "CCCCCCCCCIYY")] // 27 Oct 00:00 in Riyadh: Oct 2026 starts
    [InlineData("2026-01-26T09:00:00Z", "YYYYYYYYYYYY")] // before the FY starts
    [InlineData("2026-01-26T21:00:00Z", "IYYYYYYYYYYY")] // 27 Jan 00:00 in Riyadh: the first day
    [InlineData("2027-01-26T20:59:59Z", "CCCCCCCCCCCI")] // 26 Jan 2027 23:59:59 in Riyadh: the last day
    [InlineData("2027-01-27T09:00:00Z", "CCCCCCCCCCCC")] // after the FY ends
    public async Task Statuses_follow_today_in_the_profile_time_zone(string utcNow, string expected)
    {
        var sub = await SetUpUser(factory);

        var overview = await GetOk(sub, utcNow: DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture));

        var statuses = string.Concat(overview["months"]!.AsArray().Select(month => month!["status"]!.GetValue<string>() switch
        {
            "closed" => 'C',
            "inProgress" => 'I',
            "yetToCome" => 'Y',
            var other => throw new InvalidOperationException(other),
        }));
        Assert.Equal(expected, statuses);
    }

    // M7 "Done when": every grid total equals the Entries total behind its MON-04 link.
    [Fact]
    public async Task Grid_totals_equal_the_entries_totals()
    {
        var sub = await SetUp();
        var overview = await GetOk(sub);

        async Task AssertSame(decimal spent, string query)
        {
            var response = await Send(factory, HttpMethod.Get, $"/api/entries?fyId={Fy}" + query, sub);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(spent, Dec((await Json(response))["total"]));
        }

        var checks = 0;
        foreach (var row in overview["rows"]!.AsArray())
        {
            var categoryId = row!["categoryId"]!.GetValue<string>();
            foreach (var cell in row["months"]!.AsArray())
            {
                await AssertSame(Dec(cell!["spent"]), $"&periodKey={cell["periodKey"]}&categoryId={categoryId}");
                checks++;
            }
            await AssertSame(Dec(row["spent"]), $"&categoryId={categoryId}");
            foreach (var subRow in row["subCategories"]!.AsArray())
            {
                var subCategoryId = subRow!["subCategoryId"]!.GetValue<string>();
                foreach (var cell in subRow["months"]!.AsArray())
                {
                    await AssertSame(Dec(cell!["spent"]), $"&periodKey={cell["periodKey"]}&subCategoryId={subCategoryId}");
                    checks++;
                }
                await AssertSame(Dec(subRow["spent"]), $"&subCategoryId={subCategoryId}");
            }
        }
        foreach (var month in overview["months"]!.AsArray())
        {
            await AssertSame(Dec(month!["spent"]), $"&periodKey={month["period"]!["key"]}");
            checks++;
        }
        await AssertSame(Dec(overview["totals"]!["spent"]), "");
        Assert.Equal(12 * 5, checks);
    }

    // Owner's choice (M7): a sub-category appears when it has a plan or spending in the FY, archived
    // or not; a category appears when one of its sub-categories does. Rows follow sortOrder.
    [Fact]
    public async Task Rows_show_only_sub_categories_with_a_plan_or_spending()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddCategoryAsync(sub, new Category("health", "Health", 2));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("doctor", "health", "Doctor", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("snacks", "food", "Snacks", 2));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("rent", "food", "Rent", 1));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("taxi", "transport", "Taxi", 0, Archived: true));
        await factory.Store.SetPlanAsync(sub, Fy, new PlanAmounts("snacks", "food", new Dictionary<string, long> { ["2026-03"] = 5_000 }));
        await AddEntry(sub, "groceries", "2026-10-01", 20m);
        // The API refuses new entries on an archived sub-category, so this one predates the archive.
        await factory.Store.AddEntryAsync(sub, new Entry("t", new DateOnly(2026, 5, 3), "2026-04", Fy, "transport", "taxi", "bank", 3_000, ""));

        var overview = await GetOk(sub);

        Assert.Equal(["food", "transport"], overview["rows"]!.AsArray().Select(row => row!["categoryId"]!.GetValue<string>()));
        Assert.Equal(["groceries", "snacks"], Row(overview, "food")["subCategories"]!.AsArray().Select(row => row!["subCategoryId"]!.GetValue<string>()));
        Assert.Equal(30m, Dec(SubRow(overview, "transport", "taxi")["spent"]));
        // The names and the archived flag come with the overview, exactly as GET /api/categories (NFR-01).
        var categories = await Json(await Send(factory, HttpMethod.Get, "/api/categories", sub));
        Assert.Equal(categories.ToJsonString(), overview["categories"]!.ToJsonString());
        var taxi = overview["categories"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == "transport")!["subCategories"]![0]!;
        Assert.True(taxi["archived"]!.GetValue<bool>());
    }

    // R-CAL-09: each FY counts only its own months and entries; the FY list is oldest first (MON-03).
    [Fact]
    public async Task Another_fiscal_year_has_its_own_months_and_entries()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("fuel", "transport", "Fuel", 0));
        var fy2026 = (await factory.Store.GetFiscalYearsAsync(sub)).Single();
        // Start month changed to July: a 6-month transition FY, Jan to Jun 2027 (R-CAL-08 example A).
        await factory.Store.AddFiscalYear(sub, FiscalCalendar.NextFiscalYear(fy2026, 7, 27));
        await AddEntry(sub, "fuel", "2026-10-01", 100m);
        await AddEntry(sub, "fuel", "2027-02-10", 75m);

        var next = await GetOk(sub, "2027-01");
        var current = await GetOk(sub);

        Assert.Equal(["2026-01", "2027-01"], next["fiscalYears"]!.AsArray().Select(fy => fy!["id"]!.GetValue<string>()));
        Assert.Equal(6, next["months"]!.AsArray().Count);
        Assert.Equal(75m, Dec(next["totals"]!["spent"]));
        Assert.Equal(75m, Dec(Month(next, "2027-01")["spent"]));
        Assert.All(next["months"]!.AsArray(), month => Assert.Equal("yetToCome", month!["status"]!.GetValue<string>()));
        Assert.Equal(100m, Dec(current["totals"]!["spent"]));
    }

    // R-MON-01: the sums are made in minor units, so 0.10 + 0.20 is exactly 0.30.
    [Fact]
    public async Task Sums_are_exact()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await AddEntry(sub, "groceries", "2026-10-01", 0.10m);
        await AddEntry(sub, "groceries", "2026-10-02", 0.20m);

        var overview = await GetOk(sub);

        Assert.Equal(0.30m, Dec(Month(overview, "2026-09")["spent"]));
        Assert.Equal(0.30m, Dec(Cell(SubRow(overview, "food", "groceries"), "2026-09")["spent"]));
        Assert.Equal(-0.30m, Dec(overview["totals"]!["remaining"]));
    }

    // NFR-03: an unknown FY, or one only another user has, is 404.
    [Fact]
    public async Task Unknown_or_another_users_fiscal_year_is_404()
    {
        var owner = await SetUpUser(factory);
        var fy2026 = (await factory.Store.GetFiscalYearsAsync(owner)).Single();
        await factory.Store.AddFiscalYear(owner, FiscalCalendar.NextFiscalYear(fy2026, 1, 27));
        var other = await SetUpUser(factory);

        Assert.Equal(HttpStatusCode.OK, (await Get(owner, "2027-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(other, "2027-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(owner, "2030-01")).StatusCode);
    }

    [Fact]
    public async Task Overview_without_a_token_is_401()
    {
        var response = await factory.CreateClient().GetAsync($"/api/fiscal-years/{Fy}/overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
