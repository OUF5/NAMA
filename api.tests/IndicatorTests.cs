using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.4 GET /api/indicator (ENT-01): planned, spent so far, remaining and percent for one
// sub-category in the month of a date, the month on its own (owner note 32), and what went over plan
// last month and is not made up yet (R-PLN-09). Today is 17 Oct 2026; FY 2026 has payday 27.
public class IndicatorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // The hand check of the M5 "Done when":
    // Groceries planned 1,500 in Oct 2026 (27 Sep to 26 Oct) and 1,000 in Sep 2026 (27 Aug to 26 Sep).
    // Entries: Groceries 400 on 1 Oct and 799.99 on 17 Oct (Sep), Groceries 50 on 10 Sep (Aug),
    // and Fuel 100 on 1 Oct (Sep, another sub-category).
    // Aug: planned 1,000, spent 50, remaining 950, percent 5.
    // Sep on its own (owner note 32; Aug's 950 left does not carry): planned 1,500, spent 400 + 799.99 =
    // 1,199.99, remaining 300.01, percent 79 (79.99 rounded down); nothing overspent in Aug.
    private async Task<string> SetUp()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("fuel", "transport", "Fuel", 0));
        await factory.Store.SetPlanAsync(sub, "2026-01",
            new PlanAmounts("groceries", "food", new Dictionary<string, long> { ["2026-09"] = 150_000, ["2026-08"] = 100_000 }));
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

    private Task<HttpResponseMessage> Get(string sub, string subCategoryId, string date) =>
        Send(factory, HttpMethod.Get, $"/api/indicator?subCategoryId={subCategoryId}&date={date}", sub);

    private async Task<JsonNode> GetOk(string sub, string subCategoryId, string date)
    {
        var response = await Get(sub, subCategoryId, date);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static (decimal, decimal, decimal, int?, decimal) Numbers(JsonNode indicator) => (
        indicator["planned"]!.GetValue<decimal>(),
        indicator["spent"]!.GetValue<decimal>(),
        indicator["remaining"]!.GetValue<decimal>(),
        indicator["percent"]?.GetValue<int>(),
        indicator["overspentLastMonth"]!.GetValue<decimal>());

    [Fact]
    public async Task Numbers_match_the_hand_check()
    {
        var sub = await SetUp();

        var indicator = await GetOk(sub, "groceries", "2026-10-17");

        Assert.Equal((1500m, 1199.99m, 300.01m, (int?)79, 0m), Numbers(indicator));
        Assert.Equal(
            """{"key":"2026-09","label":"Oct 2026","startDate":"2026-09-27","endDate":"2026-10-26"}""",
            indicator["period"]!.ToJsonString());
    }

    // Any date of the month gives the same month; a closed month works too (R-STA-02).
    [Theory]
    [InlineData("2026-09-27", "2026-09", "1500", "1199.99", "300.01", 79)]
    [InlineData("2026-10-26", "2026-09", "1500", "1199.99", "300.01", 79)]
    [InlineData("2026-09-26", "2026-08", "1000", "50", "950", 5)]
    public async Task The_month_comes_from_the_date(
        string date, string periodKey, string planned, string spent, string remaining, int percent)
    {
        static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var sub = await SetUp();

        var indicator = await GetOk(sub, "groceries", date);

        Assert.Equal(periodKey, indicator["period"]!["key"]!.GetValue<string>());
        Assert.Equal((D(planned), D(spent), D(remaining), (int?)percent, 0m), Numbers(indicator));
    }

    // ENT-01 "No plan for this month": nothing planned and no percent; spent still counts.
    [Fact]
    public async Task Without_a_plan_percent_is_null()
    {
        var sub = await SetUp();

        Assert.Equal((0m, 100m, -100m, (int?)null, 0m), Numbers(await GetOk(sub, "fuel", "2026-10-17")));
    }

    // R-PLN-09: Sep's 100 on Fuel without a plan is overspent last month in Oct, beside the month on
    // its own. Two months later it is not counted: only the last month is (owner's choice).
    [Fact]
    public async Task Last_months_overspend_is_said_beside_the_month()
    {
        var sub = await SetUp();

        Assert.Equal((0m, 0m, 0m, (int?)null, 100m), Numbers(await GetOk(sub, "fuel", "2026-10-27")));
        Assert.Equal((0m, 0m, 0m, (int?)null, 0m), Numbers(await GetOk(sub, "fuel", "2026-11-27")));
    }

    // R-PLN-09: the FY's first month has no month before it to make up, and a month under plan
    // last month leaves nothing.
    [Fact]
    public async Task Nothing_is_overspent_before_the_fiscal_year_or_after_a_month_under_plan()
    {
        var sub = await SetUp();
        await AddEntry(sub, "groceries", "2026-01-28", 30m);

        Assert.Equal(0m, Numbers(await GetOk(sub, "groceries", "2026-01-28")).Item5);
        Assert.Equal(0m, Numbers(await GetOk(sub, "groceries", "2026-10-17")).Item5);
    }

    [Fact]
    public async Task Unknown_sub_category_gets_404()
    {
        var sub = await SetUp();
        var other = await SetUpUser(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await Get(sub, "nope", "2026-10-17")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(other, "groceries", "2026-10-17")).StatusCode);
    }

    [Theory]
    [InlineData("2026-01-26")]
    [InlineData("2027-01-27")]
    public async Task Date_outside_every_fiscal_year_gets_422(string date)
    {
        var sub = await SetUp();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Get(sub, "groceries", date)).StatusCode);
    }
}
