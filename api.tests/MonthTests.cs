using System.Net;
using System.Text.Json.Nodes;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// Owner note 9: GET /api/fiscal-years/{fyId}/months/{periodKey}, Tracker's one month. FY 2026 has
// payday 27, so Sep 2026 runs 27 Sep to 26 Oct and Aug 2026 27 Aug to 26 Sep.
public class MonthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Fy = "2026-01";

    // Sep: 10 (27 Sep, SNB), 400 and Fuel 100 (1 Oct, SNB, Fuel added later), 20 (5 Oct, Al Rajhi),
    // 799.99 (17 Oct, Al Rajhi), Fuel 30 (20 Oct, Al Rajhi). Aug: 50 (10 Sep) and Fuel 200 (20 Sep).
    private async Task<string> SetUp()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("fuel", "transport", "Fuel", 0));
        await AddEntry(sub, "groceries", "2026-09-10", 50m);
        await AddEntry(sub, "fuel", "2026-09-20", 200m);
        await AddEntry(sub, "groceries", "2026-09-27", 10m);
        await AddEntry(sub, "groceries", "2026-10-01", 400m);
        await AddEntry(sub, "fuel", "2026-10-01", 100m);
        await AddEntry(sub, "groceries", "2026-10-05", 20m, "rajhi");
        await AddEntry(sub, "groceries", "2026-10-17", 799.99m, "rajhi");
        await AddEntry(sub, "fuel", "2026-10-20", 30m, "rajhi");
        return sub;
    }

    private async Task AddEntry(string sub, string subCategoryId, string date, decimal amount, string account = "bank")
    {
        var response = await Send(factory, HttpMethod.Post, "/api/entries", sub, new JsonObject
        {
            ["subCategoryId"] = subCategoryId,
            ["bankAccountId"] = account,
            ["date"] = date,
            ["amount"] = amount,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<JsonNode> GetOk(string sub, string periodKey, string query = "")
    {
        var response = await Send(factory, HttpMethod.Get, $"/api/fiscal-years/{Fy}/months/{periodKey}{query}", sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static decimal Dec(JsonNode? node) => node!.GetValue<decimal>();

    private static IEnumerable<decimal> Amounts(JsonNode? entries) => entries!.AsArray().Select(entry => Dec(entry!["amount"]));

    [Fact]
    public async Task The_month_matches_the_hand_check()
    {
        var month = await GetOk(await SetUp(), "2026-09");

        Assert.Equal(("2026-09", "2026-08"), (month["period"]!["key"]!.GetValue<string>(), month["lastMonth"]!["key"]!.GetValue<string>()));
        Assert.Equal((1_359.99m, 6), (Dec(month["total"]), month["count"]!.GetValue<int>()));

        var days = month["days"]!.AsArray();
        Assert.Equal(30, days.Count);
        var spentDays = days.Where(day => Dec(day!["spent"]) != 0).Select(day => (day!["date"]!.GetValue<string>(), Dec(day["spent"])));
        Assert.Equal(
            [("2026-09-27", 10m), ("2026-10-01", 500m), ("2026-10-05", 20m), ("2026-10-17", 799.99m), ("2026-10-20", 30m)],
            spentDays);

        Assert.Equal([("rajhi", 849.99m), ("bank", 510m)],
            month["accounts"]!.AsArray().Select(account => (account!["bankAccountId"]!.GetValue<string>(), Dec(account["spent"]))));
        // Food 10 + 400 + 20 + 799.99 against Aug's 50; Transport 100 + 30 against 200.
        Assert.Equal([("food", 1_229.99m, 50m), ("transport", 130m, 200m)],
            month["categories"]!.AsArray().Select(c => (c!["categoryId"]!.GetValue<string>(), Dec(c["spent"]), Dec(c["lastSpent"]))));

        // Newest first; on 1 Oct Fuel was added after the 400.
        Assert.Equal([30m, 799.99m, 20m, 100m, 400m], Amounts(month["latest"]));
        Assert.Equal([799.99m, 400m, 100m, 30m, 20m], Amounts(month["biggest"]));
        Assert.Equal(["bank", "rajhi"], month["bankAccounts"]!.AsArray().Select(account => account!["id"]!.GetValue<string>()).Order());
    }

    // Tracker's filter narrows every part.
    [Fact]
    public async Task A_category_narrows_the_month()
    {
        var month = await GetOk(await SetUp(), "2026-09", "?categoryId=transport");

        Assert.Equal((130m, 2), (Dec(month["total"]), month["count"]!.GetValue<int>()));
        Assert.Equal([("bank", 100m), ("rajhi", 30m)],
            month["accounts"]!.AsArray().Select(account => (account!["bankAccountId"]!.GetValue<string>(), Dec(account["spent"]))));
        Assert.Equal([("transport", 130m, 200m)],
            month["categories"]!.AsArray().Select(c => (c!["categoryId"]!.GetValue<string>(), Dec(c["spent"]), Dec(c["lastSpent"]))));
        Assert.Equal([30m, 100m], Amounts(month["latest"]));
    }

    [Fact]
    public async Task The_first_stored_month_has_no_month_before()
    {
        var month = await GetOk(await SetUp(), "2026-01");

        Assert.Null(month["lastMonth"]);
        Assert.Equal((0m, 0), (Dec(month["total"]), month["count"]!.GetValue<int>()));
    }

    [Theory]
    [InlineData("/api/fiscal-years/2027-01/months/2027-01")]
    [InlineData("/api/fiscal-years/2026-01/months/2027-01")]
    public async Task An_unknown_fiscal_year_or_month_gets_404(string url)
    {
        var response = await Send(factory, HttpMethod.Get, url, await SetUpUser(factory));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_gets_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/fiscal-years/2026-01/months/2026-09")).StatusCode);
    }
}
