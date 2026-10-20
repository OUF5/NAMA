using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.4 GET /api/indicator (ENT-01): planned, spent so far, remaining and percent for one
// sub-category in the month of a date, with what carried in from the FY's earlier months (R-PLN-05).
// Today is 17 Oct 2026; FY 2026 has payday 27.
public class IndicatorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // The hand check of the M5 "Done when":
    // Groceries planned 1,500 in Sep 2026 (27 Sep to 26 Oct) and 1,000 in Aug 2026 (27 Aug to 26 Sep).
    // Entries: Groceries 400 on 1 Oct and 799.99 on 17 Oct (Sep), Groceries 50 on 10 Sep (Aug),
    // and Fuel 100 on 1 Oct (Sep, another sub-category).
    // Aug: planned 1,000, spent 50, remaining 950, percent 5.
    // Sep: Aug's 950 carries in, so available is 950 + 1,500 = 2,450; spent 400 + 799.99 = 1,199.99;
    // remaining 1,250.01; percent 48 (48.98 rounded down).
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

    private static (decimal, decimal, decimal, decimal, decimal, int?) Numbers(JsonNode indicator) => (
        indicator["carriedIn"]!.GetValue<decimal>(),
        indicator["planned"]!.GetValue<decimal>(),
        indicator["available"]!.GetValue<decimal>(),
        indicator["spent"]!.GetValue<decimal>(),
        indicator["remaining"]!.GetValue<decimal>(),
        indicator["percent"]?.GetValue<int>());

    [Fact]
    public async Task Numbers_match_the_hand_check()
    {
        var sub = await SetUp();

        var indicator = await GetOk(sub, "groceries", "2026-10-17");

        Assert.Equal((950m, 1500m, 2450m, 1199.99m, 1250.01m, (int?)48), Numbers(indicator));
        Assert.Equal(
            """{"key":"2026-09","label":"Sep 2026","startDate":"2026-09-27","endDate":"2026-10-26"}""",
            indicator["period"]!.ToJsonString());
    }

    // Any date of the month gives the same month; a closed month works too (R-STA-02).
    [Theory]
    [InlineData("2026-09-27", "2026-09", "950", "1500", "1199.99", "1250.01", 48)]
    [InlineData("2026-10-26", "2026-09", "950", "1500", "1199.99", "1250.01", 48)]
    [InlineData("2026-09-26", "2026-08", "0", "1000", "50", "950", 5)]
    public async Task The_month_comes_from_the_date(
        string date, string periodKey, string carriedIn, string planned, string spent, string remaining, int percent)
    {
        static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var sub = await SetUp();

        var indicator = await GetOk(sub, "groceries", date);

        Assert.Equal(periodKey, indicator["period"]!["key"]!.GetValue<string>());
        Assert.Equal((D(carriedIn), D(planned), D(carriedIn) + D(planned), D(spent), D(remaining), (int?)percent), Numbers(indicator));
    }

    // ENT-01 "No plan for this month": nothing available and no percent; spent still counts.
    [Fact]
    public async Task Without_a_plan_percent_is_null()
    {
        var sub = await SetUp();

        Assert.Equal((0m, 0m, 0m, 100m, -100m, (int?)null), Numbers(await GetOk(sub, "fuel", "2026-10-17")));
    }

    // R-PLN-05: Sep's overspend on Fuel (100 without a plan) carries into Oct as it is.
    [Fact]
    public async Task An_overspend_carries_into_the_next_month()
    {
        var sub = await SetUp();

        Assert.Equal((-100m, 0m, -100m, 0m, -100m, (int?)null), Numbers(await GetOk(sub, "fuel", "2026-10-27")));
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
