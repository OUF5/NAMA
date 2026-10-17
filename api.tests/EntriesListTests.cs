using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.5 GET /api/entries: the entries of one FY, newest first (TBL-01), with filters and the
// total of the filtered rows (TBL-04). Today is 17 Oct 2026; FY 2026 has payday 27.
public class EntriesListTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Posted in this order:
    // a: Groceries 400 on 1 Oct (Sep 2026), SNB, "Weekly shop"
    // b: Groceries 799.99 on 17 Oct (Sep 2026), Al Rajhi, "Big SHOP"
    // c: Groceries 50 on 10 Sep (Aug 2026), SNB, no note
    // d: Fuel 100 on 1 Oct (Sep 2026), Al Rajhi, "Fuel"
    // Newest first: b, then d and a (same date, d was added later), then c.
    private async Task<(string Sub, Dictionary<string, string> Ids)> SetUp()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("fuel", "transport", "Fuel", 0));
        var ids = new Dictionary<string, string>
        {
            ["a"] = await AddEntry(sub, "groceries", "bank", "2026-10-01", 400m, "Weekly shop"),
            ["b"] = await AddEntry(sub, "groceries", "rajhi", "2026-10-17", 799.99m, "Big SHOP"),
            ["c"] = await AddEntry(sub, "groceries", "bank", "2026-09-10", 50m, null),
            ["d"] = await AddEntry(sub, "fuel", "rajhi", "2026-10-01", 100m, "Fuel"),
        };
        return (sub, ids);
    }

    private async Task<string> AddEntry(string sub, string subCategoryId, string accountId, string date, decimal amount, string? note)
    {
        var response = await Send(factory, HttpMethod.Post, "/api/entries", sub, new JsonObject
        {
            ["subCategoryId"] = subCategoryId,
            ["bankAccountId"] = accountId,
            ["date"] = date,
            ["amount"] = amount,
            ["note"] = note,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response))["id"]!.GetValue<string>();
    }

    private async Task<JsonNode> GetOk(string sub, string query = "")
    {
        var response = await Send(factory, HttpMethod.Get, "/api/entries" + query, sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static List<string> Ids(JsonNode list) =>
        list["entries"]!.AsArray().Select(entry => entry!["id"]!.GetValue<string>()).ToList();

    [Fact]
    public async Task Lists_the_current_fiscal_year_newest_first_with_the_total()
    {
        var (sub, ids) = await SetUp();

        var list = await GetOk(sub);

        Assert.Equal("2026-01", list["fyId"]!.GetValue<string>());
        Assert.Equal([ids["b"], ids["d"], ids["a"], ids["c"]], Ids(list));
        Assert.Equal(1_349.99m, list["total"]!.GetValue<decimal>());
        var first = list["entries"]![0]!;
        Assert.Equal(("2026-10-17", "2026-09", "food", "groceries", "rajhi", 799.99m, "Big SHOP"), (
            first["date"]!.GetValue<string>(), first["periodKey"]!.GetValue<string>(), first["categoryId"]!.GetValue<string>(),
            first["subCategoryId"]!.GetValue<string>(), first["bankAccountId"]!.GetValue<string>(),
            first["amount"]!.GetValue<decimal>(), first["note"]!.GetValue<string>()));
        // The FY selector, the month filter and the account names come with the list (NFR-01).
        var fiscalYear = list["fiscalYears"]!.AsArray().Single()!;
        Assert.Equal(("2026-01", "FY 2026", 12), (fiscalYear["id"]!.GetValue<string>(), fiscalYear["label"]!.GetValue<string>(), fiscalYear["periods"]!.AsArray().Count));
        Assert.Equal(["Al Rajhi", "SNB"], list["bankAccounts"]!.AsArray().Select(account => account!["name"]!.GetValue<string>()));
    }

    // TBL-04: each filter alone and combined; the total is the sum of the filtered rows.
    [Theory]
    [InlineData("?periodKey=2026-08", "c", "50")]
    [InlineData("?periodKey=2026-09", "b,d,a", "1299.99")]
    [InlineData("?categoryId=transport", "d", "100")]
    [InlineData("?subCategoryId=groceries", "b,a,c", "1249.99")]
    [InlineData("?bankAccountId=rajhi", "b,d", "899.99")]
    [InlineData("?q=shop", "b,a", "1199.99")]
    [InlineData("?q=%20sHoP%20", "b,a", "1199.99")]
    [InlineData("?fyId=2026-01&periodKey=2026-09&subCategoryId=groceries&bankAccountId=bank", "a", "400")]
    [InlineData("?subCategoryId=nope", "", "0")]
    public async Task Filters_narrow_the_rows_and_the_total(string query, string expected, string total)
    {
        var (sub, ids) = await SetUp();

        var list = await GetOk(sub, query);

        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(name => ids[name]), Ids(list));
        Assert.Equal(decimal.Parse(total, CultureInfo.InvariantCulture), list["total"]!.GetValue<decimal>());
    }

    // R-MON-01: the sum is made in minor units, so 0.10 + 0.20 is exactly 0.30.
    [Fact]
    public async Task Total_is_exact()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await AddEntry(sub, "groceries", "bank", "2026-10-01", 0.10m, null);
        await AddEntry(sub, "groceries", "bank", "2026-10-02", 0.20m, null);

        Assert.Equal(0.30m, (await GetOk(sub))["total"]!.GetValue<decimal>());
    }

    // TBL-01 "selected FY": the one that contains today by default; another one by fyId.
    [Fact]
    public async Task Another_fiscal_year_is_chosen_with_fyId()
    {
        var (sub, _) = await SetUp();
        var fy2026 = (await factory.Store.GetFiscalYearsAsync(sub)).Single();
        await factory.Store.AddFiscalYear(sub, FiscalCalendar.NextFiscalYear(fy2026, 1, 27));
        var february = await AddEntry(sub, "fuel", "bank", "2027-02-10", 75m, null);

        var current = await GetOk(sub);
        var next = await GetOk(sub, "?fyId=2027-01");

        Assert.Equal(["2026-01", "2027-01"], current["fiscalYears"]!.AsArray().Select(fy => fy!["id"]!.GetValue<string>()));
        Assert.Equal(("2026-01", 4), (current["fyId"]!.GetValue<string>(), Ids(current).Count));
        Assert.Equal(("2027-01", 75m), (next["fyId"]!.GetValue<string>(), next["total"]!.GetValue<decimal>()));
        Assert.Equal([february], Ids(next));
    }

    // After the last FY ends (rollover is M8), the latest FY is shown.
    [Fact]
    public async Task Without_a_current_fiscal_year_the_latest_is_shown()
    {
        var fy2024 = FiscalCalendar.FirstFiscalYear(new DateOnly(2024, 10, 17), 1, 27);
        var sub = await SetUpUser(factory, fiscalYear: fy2024);
        await factory.Store.AddFiscalYear(sub, FiscalCalendar.NextFiscalYear(fy2024, 1, 27));

        var list = await GetOk(sub);

        Assert.Equal("2025-01", list["fyId"]!.GetValue<string>());
        Assert.Empty(Ids(list));
        Assert.Equal(0m, list["total"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Unknown_fiscal_year_gets_404()
    {
        var (sub, _) = await SetUp();

        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Get, "/api/entries?fyId=2030-01", sub)).StatusCode);
    }

    // NFR-03: every query is scoped to the user from the token.
    [Fact]
    public async Task Another_users_entries_are_not_listed()
    {
        await SetUp();
        var other = await SetUpUser(factory);

        Assert.Empty(Ids(await GetOk(other)));
    }

    [Fact]
    public async Task Without_a_token_gets_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/entries")).StatusCode);
    }
}
