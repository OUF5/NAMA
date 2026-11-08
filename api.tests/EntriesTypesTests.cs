using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// TBL-07 (owner note 33): GET /api/entries lists expenses, transfers and additional income, each
// newest first with its total, filtered by type and by the TBL-04 filters. Today is 17 Oct 2026;
// FY 2026 has payday 27, so Aug 2026 starts on 27 Aug and Sep 2026 on 27 Sep.
public class EntriesTypesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Fy = "2026-01";

    // Groceries 400 on 1 Oct from SNB ("Weekly shop"); a transfer of 250 from SNB to Al Rajhi on 5 Oct
    // ("Top up shop card"); a Gift of 500 into Al Rajhi in Sep (Once, Actual); a Bonus of 300 a month in
    // Aug and Sep (Monthly, Planned: no account); the salary, which is not additional income.
    private async Task<string> SetUp()
    {
        var sub = await SetUpUser(factory, incomes: [new IncomeItem("s", IncomeKind.Salary, "Salary", 1_700_000, "2026-01", null)]);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddEntryAsync(sub, new Entry("e1", new DateOnly(2026, 10, 1), "2026-09", Fy, "food", "groceries", "bank", 40_000, "Weekly shop"));
        await factory.Store.AddTransferAsync(sub, new Transfer("t1", new DateOnly(2026, 10, 5), "2026-09", Fy, "bank", "rajhi", 25_000, "Top up shop card"));
        await factory.Store.AddIncomeAsync(sub, new IncomeItem("gift", IncomeKind.Additional, "Gift", 50_000, "2026-09", "2026-09", IncomeCounts.Actual, "rajhi"));
        await factory.Store.AddIncomeAsync(sub, new IncomeItem("bonus", IncomeKind.Additional, "Bonus", 30_000, "2026-08", "2026-09", IncomeCounts.Planned));
        return sub;
    }

    private async Task<JsonNode> GetOk(string sub, string query = "")
    {
        var response = await Send(factory, HttpMethod.Get, "/api/entries" + query, sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static List<string> Ids(JsonNode list, string type, string id = "id") =>
        list[type]!.AsArray().Select(row => row![id]!.GetValue<string>()).ToList();

    // "gift 2026-09": an income row is its item and its month.
    private static List<string> IncomeRows(JsonNode list) =>
        list["income"]!.AsArray().Select(row => $"{row!["incomeId"]!.GetValue<string>()} {row["periodKey"]!.GetValue<string>()}").ToList();

    private static (decimal, decimal, decimal) Totals(JsonNode list) =>
        (list["total"]!.GetValue<decimal>(), list["transferTotal"]!.GetValue<decimal>(), list["incomeTotal"]!.GetValue<decimal>());

    [Fact]
    public async Task Lists_every_type_with_its_total()
    {
        var list = await GetOk(await SetUp());

        Assert.Equal(["e1"], Ids(list, "entries"));
        Assert.Equal(["t1"], Ids(list, "transfers"));
        // Newest first, then by label; a Monthly item has a row for each of its months; no salary.
        Assert.Equal(["bonus 2026-09", "gift 2026-09", "bonus 2026-08"], IncomeRows(list));
        Assert.Equal((400m, 250m, 1_100m), Totals(list));

        var transfer = list["transfers"]![0]!;
        Assert.Equal(("2026-10-05", "bank", "rajhi", 250m, "Top up shop card"), (
            transfer["date"]!.GetValue<string>(), transfer["fromBankAccountId"]!.GetValue<string>(),
            transfer["toBankAccountId"]!.GetValue<string>(), transfer["amount"]!.GetValue<decimal>(), transfer["note"]!.GetValue<string>()));
        // Dated its month's payday, with what it counts in and its account.
        var gift = list["income"]![1]!;
        Assert.Equal(("2026-09-27", "Gift", "actual", "rajhi", 500m), (
            gift["date"]!.GetValue<string>(), gift["label"]!.GetValue<string>(), gift["counts"]!.GetValue<string>(),
            gift["bankAccountId"]!.GetValue<string>(), gift["amount"]!.GetValue<decimal>()));
        Assert.Null(list["income"]![0]!["bankAccountId"]);
    }

    // TBL-07: type keeps one type; a category keeps expenses only; a bank account keeps what was paid
    // from it, moved into or out of it and received into it (a Planned item has none); q searches
    // notes and income labels; a month keeps that month's rows.
    [Theory]
    [InlineData("?type=expense", "e1", "", "")]
    [InlineData("?type=transfer", "", "t1", "")]
    [InlineData("?type=income", "", "", "bonus 2026-09,gift 2026-09,bonus 2026-08")]
    [InlineData("?categoryId=food", "e1", "", "")]
    [InlineData("?subCategoryId=groceries", "e1", "", "")]
    [InlineData("?bankAccountId=rajhi", "", "t1", "gift 2026-09")]
    [InlineData("?bankAccountId=bank", "e1", "t1", "")]
    [InlineData("?q=SHOP", "e1", "t1", "")]
    [InlineData("?q=gift", "", "", "gift 2026-09")]
    [InlineData("?periodKey=2026-08", "", "", "bonus 2026-08")]
    [InlineData("?type=income&bankAccountId=rajhi", "", "", "gift 2026-09")]
    public async Task Filters_keep_the_rows_of_each_type(string query, string entries, string transfers, string income)
    {
        static List<string> Split(string names) => names.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        var list = await GetOk(await SetUp(), query);

        Assert.Equal(Split(entries), Ids(list, "entries"));
        Assert.Equal(Split(transfers), Ids(list, "transfers"));
        Assert.Equal(Split(income), IncomeRows(list));
    }

    [Fact]
    public async Task An_unknown_type_gets_400()
    {
        var sub = await SetUp();

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(factory, HttpMethod.Get, "/api/entries?type=loan", sub)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(factory, HttpMethod.Get, "/api/entries/export.csv?type=loan", sub)).StatusCode);
    }

    // TBL-06: every type in one list, newest first, with its Type and Details as on the page.
    [Fact]
    public async Task The_csv_has_every_type()
    {
        var response = await Send(factory, HttpMethod.Get, "/api/entries/export.csv", await SetUp());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = Encoding.UTF8.GetString((await response.Content.ReadAsByteArrayAsync())[3..]);

        Assert.Equal(
            "\"Date\",\"Month\",\"Type\",\"Details\",\"Amount (SAR)\",\"Bank account\",\"Note\"\r\n" +
            "\"2026-10-05\",\"Oct 2026\",\"Transfer\",\"\",\"250.00\",\"SNB → Al Rajhi\",\"Top up shop card\"\r\n" +
            "\"2026-10-01\",\"Oct 2026\",\"Expense\",\"Food · Groceries\",\"400.00\",\"SNB\",\"Weekly shop\"\r\n" +
            "\"2026-09-27\",\"Oct 2026\",\"Income\",\"Bonus · Planned\",\"300.00\",\"\",\"\"\r\n" +
            "\"2026-09-27\",\"Oct 2026\",\"Income\",\"Gift · Actual\",\"500.00\",\"Al Rajhi\",\"\"\r\n" +
            "\"2026-08-27\",\"Sep 2026\",\"Income\",\"Bonus · Planned\",\"300.00\",\"\",\"\"\r\n",
            csv);
    }
}
