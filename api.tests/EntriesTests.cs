using System.Net;
using System.Text.Json.Nodes;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.4 POST /api/entries: the API assigns the month and FY from the date (R-CAL-09, R-STA-03),
// checks R-MON-02 and PRD 4.7, and remembers the account (decision 5).
// Today is 17 Oct 2026 in Riyadh; FY 2026 runs 27 Jan 2026 to 26 Jan 2027 with payday 27.
public class EntriesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<string> SetUp()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("old", "food", "Old", 1, Archived: true));
        await factory.Store.AddBankAccount(sub, new BankAccount("closed", "Closed account", Archived: true));
        return sub;
    }

    private static JsonObject Body(string date = "2026-10-17", decimal amount = 125.50m, string? note = null) => new()
    {
        ["subCategoryId"] = "groceries",
        ["bankAccountId"] = "rajhi",
        ["date"] = date,
        ["amount"] = amount,
        ["note"] = note,
    };

    private Task<HttpResponseMessage> Post(string sub, JsonObject body) =>
        Send(factory, HttpMethod.Post, "/api/entries", sub, body);

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string field)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await Json(response);
        Assert.True(problem["errors"]!.AsObject().ContainsKey(field), problem.ToJsonString());
    }

    // R-CAL-09: month boundaries of the section 3 example. Jan 2026 is closed and still takes entries (R-STA-02).
    [Theory]
    [InlineData("2026-10-17", "2026-09")]
    [InlineData("2026-10-26", "2026-09")]
    [InlineData("2026-10-27", "2026-10")]
    [InlineData("2026-01-27", "2026-01")]
    [InlineData("2027-01-26", "2026-12")]
    public async Task Each_entry_gets_the_month_of_its_date(string date, string periodKey)
    {
        var sub = await SetUp();

        var response = await Post(sub, Body(date));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = factory.Store.Entries(sub).Single();
        Assert.Equal((periodKey, "2026-01"), (stored.PeriodKey, stored.FyId));
        var created = await Json(response);
        Assert.Equal((date, periodKey, "2026-01"),
            (created["date"]!.GetValue<string>(), created["periodKey"]!.GetValue<string>(), created["fyId"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Entry_is_stored_in_minor_units_with_the_category_copied()
    {
        var sub = await SetUp();

        var response = await Post(sub, Body(note: "  Weekly shop "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = factory.Store.Entries(sub).Single();
        Assert.Equal(new Entry(stored.Id, new DateOnly(2026, 10, 17), "2026-09", "2026-01", "food", "groceries", "rajhi", 12_550, "Weekly shop"), stored);
        Assert.Equal(32, stored.Id.Length);
        var created = await Json(response);
        Assert.Equal((125.5m, "Weekly shop", "food"),
            (created["amount"]!.GetValue<decimal>(), created["note"]!.GetValue<string>(), created["categoryId"]!.GetValue<string>()));
        Assert.Equal($"/api/entries/{stored.Id}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Empty_note_is_stored_as_empty_text()
    {
        var sub = await SetUp();

        await Post(sub, Body(note: "   "));
        await Post(sub, Body());

        Assert.All(factory.Store.Entries(sub), entry => Assert.Equal("", entry.Note));
    }

    // Decision 5: the account defaults to the last used one.
    [Fact]
    public async Task Saving_remembers_the_bank_account()
    {
        var sub = await SetUp();
        Assert.Null((await factory.Store.GetProfileAsync(sub))!.LastBankAccountId);

        await Post(sub, Body());

        Assert.Equal("rajhi", (await factory.Store.GetProfileAsync(sub))!.LastBankAccountId);
    }

    // PRD 7.5: 422 for a date outside every FY (R-STA-03, FY-03).
    [Theory]
    [InlineData("2026-01-26")]
    [InlineData("2027-01-27")]
    public async Task Date_outside_every_fiscal_year_gets_422(string date)
    {
        var sub = await SetUp();

        await AssertError(await Post(sub, Body(date)), HttpStatusCode.UnprocessableEntity, "Date");
        Assert.Empty(factory.Store.Entries(sub));
    }

    public static TheoryData<string, Action<JsonObject>, string> InvalidBodies => new()
    {
        { "amount 0", b => b["amount"] = 0, "Amount" },
        { "negative amount", b => b["amount"] = -1, "Amount" },
        { "3 decimals", b => b["amount"] = 1.234m, "Amount" },
        { "over 10,000,000", b => b["amount"] = 10_000_000.01m, "Amount" },
        { "no amount", b => b.Remove("amount"), "Amount" },
        { "note over 200", b => b["note"] = new string('a', 201), "Note" },
        { "no date", b => b.Remove("date"), "Date" },
        { "no sub-category", b => b.Remove("subCategoryId"), "SubCategoryId" },
        { "unknown sub-category", b => b["subCategoryId"] = "nope", "SubCategoryId" },
        { "archived sub-category", b => b["subCategoryId"] = "old", "SubCategoryId" },
        { "no bank account", b => b.Remove("bankAccountId"), "BankAccountId" },
        { "unknown bank account", b => b["bankAccountId"] = "nope", "BankAccountId" },
        { "archived bank account", b => b["bankAccountId"] = "closed", "BankAccountId" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_entry_gets_400(string reason, Action<JsonObject> change, string field)
    {
        _ = reason;
        var sub = await SetUp();
        var body = Body();
        change(body);

        await AssertError(await Post(sub, body), HttpStatusCode.BadRequest, field);
        Assert.Empty(factory.Store.Entries(sub));
    }

    [Fact]
    public async Task Note_of_200_characters_is_allowed()
    {
        var sub = await SetUp();

        Assert.Equal(HttpStatusCode.Created, (await Post(sub, Body(note: new string('a', 200)))).StatusCode);
    }

    [Fact]
    public async Task Date_that_is_not_YYYY_MM_DD_gets_400()
    {
        var sub = await SetUp();

        Assert.Equal(HttpStatusCode.BadRequest, (await Post(sub, Body(date: "17/10/2026"))).StatusCode);
        Assert.Empty(factory.Store.Entries(sub));
    }

    // AUTH-03, NFR-03: another user's sub-category is unknown to this user.
    [Fact]
    public async Task Another_users_sub_category_is_refused()
    {
        var owner = await SetUp();
        var other = await SetUpUser(factory);

        await AssertError(await Post(other, Body()), HttpStatusCode.BadRequest, "SubCategoryId");
        Assert.Empty(factory.Store.Entries(owner));
        Assert.Empty(factory.Store.Entries(other));
    }

    [Fact]
    public async Task Without_a_token_gets_401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/entries") { Content = System.Net.Http.Json.JsonContent.Create(Body()) };
        request.Headers.Authorization = null;

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(request)).StatusCode);
    }
}
