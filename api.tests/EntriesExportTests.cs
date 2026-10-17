using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Api.Features.Entries;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.5 GET /api/entries/export.csv (TBL-06, NFR-05): the filtered entries, newest first, in the
// TBL-01 columns. Today is 17 Oct 2026; FY 2026 has payday 27.
public class EntriesExportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Header = "\"Date\",\"Month\",\"Category\",\"Sub-category\",\"Amount (SAR)\",\"Bank account\",\"Note\"\r\n";

    private async Task<string> SetUp()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("fuel", "transport", "Fuel", 0));
        return sub;
    }

    private async Task AddEntry(string sub, string subCategoryId, string accountId, string date, decimal amount, string? note)
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
    }

    private async Task<string> Export(string sub, string query = "")
    {
        var response = await Send(factory, HttpMethod.Get, "/api/entries/export.csv" + query, sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        // A UTF-8 BOM, so Excel reads names that are not ASCII.
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        return Encoding.UTF8.GetString(bytes[3..]);
    }

    [Fact]
    public async Task Exports_names_month_labels_and_decimal_amounts()
    {
        var sub = await SetUp();
        await AddEntry(sub, "fuel", "bank", "2026-10-01", 1_000m, null);
        await AddEntry(sub, "groceries", "rajhi", "2026-10-17", 125.50m, "Weekly, \"big\"\nshop");

        var response = await Send(factory, HttpMethod.Get, "/api/entries/export.csv", sub);
        var csv = await Export(sub);

        Assert.Equal(
            Header +
            "\"2026-10-17\",\"Sep 2026\",\"Food\",\"Groceries\",\"125.50\",\"Al Rajhi\",\"Weekly, \"\"big\"\"\nshop\"\r\n" +
            "\"2026-10-01\",\"Sep 2026\",\"Transport\",\"Fuel\",\"1000.00\",\"SNB\",\"\"\r\n",
            csv);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("nama-entries-2026-01.csv", response.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    // The same filters as the list (TBL-04).
    [Fact]
    public async Task Exports_only_the_filtered_entries()
    {
        var sub = await SetUp();
        await AddEntry(sub, "fuel", "bank", "2026-10-01", 1_000m, null);
        await AddEntry(sub, "groceries", "rajhi", "2026-10-17", 125.50m, "Weekly shop");
        await AddEntry(sub, "groceries", "rajhi", "2026-09-10", 50m, "Old shop");

        var csv = await Export(sub, "?periodKey=2026-09&q=SHOP");

        Assert.Equal(Header + "\"2026-10-17\",\"Sep 2026\",\"Food\",\"Groceries\",\"125.50\",\"Al Rajhi\",\"Weekly shop\"\r\n", csv);
    }

    // OWASP "CSV Injection": a note a spreadsheet would run as a formula gets a leading quote.
    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@x", "'@x")]
    [InlineData("a=b", "a=b")]
    public async Task Formulas_in_notes_are_not_run(string note, string exported)
    {
        var sub = await SetUp();
        await AddEntry(sub, "fuel", "bank", "2026-10-01", 10m, note);

        Assert.EndsWith($",\"{exported}\"\r\n", await Export(sub));
    }

    // Names are checked too; notes are trimmed, so tab and CR are tested directly.
    [Theory]
    [InlineData("\tx", "'\tx")]
    [InlineData("\rx", "'\rx")]
    [InlineData("=Food", "'=Food")]
    [InlineData("", "")]
    [InlineData("Food", "Food")]
    public void Safe_prefixes_formula_characters(string text, string safe)
    {
        Assert.Equal(safe, EntriesCsv.Safe(text));
    }

    [Fact]
    public async Task Unknown_fiscal_year_gets_404()
    {
        var sub = await SetUp();

        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Get, "/api/entries/export.csv?fyId=2030-01", sub)).StatusCode);
    }

    [Fact]
    public async Task Without_a_token_gets_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/entries/export.csv")).StatusCode);
    }
}
