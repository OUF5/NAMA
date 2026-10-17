using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.5 PUT and DELETE /api/entries/{id} (ENT-04, TBL-02, TBL-03). Today is 17 Oct 2026;
// FY 2026 runs 27 Jan 2026 to 26 Jan 2027 with payday 27.
public class EntriesEditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // The same items as EntriesTests, plus Fuel. Groceries is planned at 1,500 in Sep 2026.
    private async Task<string> SetUp()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("fuel", "transport", "Fuel", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("old", "food", "Old", 1, Archived: true));
        await factory.Store.AddBankAccount(sub, new BankAccount("closed", "Closed account", Archived: true));
        await factory.Store.SetPlanAsync(sub, "2026-01",
            new PlanAmounts("groceries", "food", new Dictionary<string, long> { ["2026-09"] = 150_000 }));
        return sub;
    }

    private static JsonObject Body(string subCategoryId = "groceries", string accountId = "rajhi", string date = "2026-10-17",
        decimal amount = 125.50m, string? note = null) => new()
    {
        ["subCategoryId"] = subCategoryId,
        ["bankAccountId"] = accountId,
        ["date"] = date,
        ["amount"] = amount,
        ["note"] = note,
    };

    private async Task<string> Add(string sub, JsonObject body)
    {
        var response = await Send(factory, HttpMethod.Post, "/api/entries", sub, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response))["id"]!.GetValue<string>();
    }

    private Task<HttpResponseMessage> Put(string sub, string id, JsonObject body) =>
        Send(factory, HttpMethod.Put, $"/api/entries/{id}", sub, body);

    private Task<HttpResponseMessage> Delete(string sub, string id) =>
        Send(factory, HttpMethod.Delete, $"/api/entries/{id}", sub);

    private Entry Stored(string sub, string id) => factory.Store.Entries(sub).Single(entry => entry.Id == id);

    // ENT-04: amount, date, sub-category, account and note. The category comes from the new sub-category.
    [Fact]
    public async Task Edit_changes_every_field()
    {
        var sub = await SetUp();
        var id = await Add(sub, Body(note: "Weekly shop"));

        var response = await Put(sub, id, Body("fuel", "bank", "2026-10-20", 99.99m, "  Fixed "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Entry(id, new DateOnly(2026, 10, 20), "2026-09", "2026-01", "transport", "fuel", "bank", 9_999, "Fixed"), Stored(sub, id));
        var edited = await Json(response);
        Assert.Equal((id, 99.99m, "transport"),
            (edited["id"]!.GetValue<string>(), edited["amount"]!.GetValue<decimal>(), edited["categoryId"]!.GetValue<string>()));
        Assert.Single(factory.Store.Entries(sub));
    }

    // ENT-04, R-CAL-09: changing the date moves the entry to the right month automatically.
    // Closed months take edits too (R-STA-02).
    [Theory]
    [InlineData("2026-10-26", "2026-09")]
    [InlineData("2026-10-27", "2026-10")]
    [InlineData("2026-01-27", "2026-01")]
    [InlineData("2027-01-26", "2026-12")]
    public async Task Changing_the_date_moves_the_month(string date, string periodKey)
    {
        var sub = await SetUp();
        var id = await Add(sub, Body());

        Assert.Equal(HttpStatusCode.OK, (await Put(sub, id, Body(date: date))).StatusCode);

        Assert.Equal((periodKey, "2026-01"), (Stored(sub, id).PeriodKey, Stored(sub, id).FyId));
    }

    [Fact]
    public async Task Changing_the_date_can_move_the_fiscal_year()
    {
        var sub = await SetUp();
        var fy2026 = (await factory.Store.GetFiscalYearsAsync(sub)).Single();
        await factory.Store.AddFiscalYear(sub, FiscalCalendar.NextFiscalYear(fy2026, 1, 27));
        var id = await Add(sub, Body());

        Assert.Equal(HttpStatusCode.OK, (await Put(sub, id, Body(date: "2027-02-10"))).StatusCode);

        Assert.Equal(("2027-01", "2027-01"), (Stored(sub, id).PeriodKey, Stored(sub, id).FyId));
    }

    // PRD 7.5: 422 outside every FY (R-STA-03); the entry stays as it was.
    [Theory]
    [InlineData("2026-01-26")]
    [InlineData("2027-01-27")]
    public async Task Date_outside_every_fiscal_year_gets_422(string date)
    {
        var sub = await SetUp();
        var id = await Add(sub, Body());
        var before = Stored(sub, id);

        var response = await Put(sub, id, Body(date: date));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await Json(response))["errors"]!.AsObject().ContainsKey("Date"));
        Assert.Equal(before, Stored(sub, id));
    }

    // The same checks as the POST (R-MON-02, PRD 4.7, archived items).
    [Theory]
    [MemberData(nameof(EntriesTests.InvalidBodies), MemberType = typeof(EntriesTests))]
    public async Task Invalid_edit_gets_400(string reason, Action<JsonObject> change, string field)
    {
        _ = reason;
        var sub = await SetUp();
        var id = await Add(sub, Body());
        var before = Stored(sub, id);
        var body = Body();
        change(body);

        var response = await Put(sub, id, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response))["errors"]!.AsObject().ContainsKey(field));
        Assert.Equal(before, Stored(sub, id));
    }

    // An entry made before its sub-category or account was archived (M8) can still be edited.
    [Fact]
    public async Task Edit_may_keep_an_archived_sub_category_and_account()
    {
        var sub = await SetUp();
        await factory.Store.AddEntryAsync(sub, new Entry("kept", new DateOnly(2026, 10, 1), "2026-09", "2026-01", "food", "old", "closed", 1_000, ""));

        Assert.Equal(HttpStatusCode.OK, (await Put(sub, "kept", Body("old", "closed", amount: 12m))).StatusCode);
        Assert.Equal(1_200, Stored(sub, "kept").AmountMinor);

        // Moving to another sub-category is allowed; moving back to the archived one is not.
        Assert.Equal(HttpStatusCode.OK, (await Put(sub, "kept", Body("groceries", "closed"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(sub, "kept", Body("old", "closed"))).StatusCode);
    }

    // Owner's choice (M6): only a new entry changes the Add form's default account.
    [Fact]
    public async Task Edit_does_not_change_the_last_used_account()
    {
        var sub = await SetUp();
        var id = await Add(sub, Body(accountId: "rajhi"));

        await Put(sub, id, Body(accountId: "bank"));

        Assert.Equal("rajhi", (await factory.Store.GetProfileAsync(sub))!.LastBankAccountId);
    }

    [Fact]
    public async Task Unknown_entry_gets_404()
    {
        var sub = await SetUp();

        Assert.Equal(HttpStatusCode.NotFound, (await Put(sub, "nope", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(sub, "nope")).StatusCode);
    }

    // AUTH-03, NFR-03: another user's entry is unknown to this user and stays unchanged.
    [Fact]
    public async Task Another_users_entry_gets_404()
    {
        var owner = await SetUp();
        var id = await Add(owner, Body());
        var before = Stored(owner, id);
        var other = await SetUp();

        Assert.Equal(HttpStatusCode.NotFound, (await Put(other, id, Body(amount: 1m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(other, id)).StatusCode);

        Assert.Equal(before, Stored(owner, id));
        Assert.Empty(factory.Store.Entries(other));
    }

    // TBL-03
    [Fact]
    public async Task Delete_removes_the_entry_once()
    {
        var sub = await SetUp();
        var id = await Add(sub, Body());
        var kept = await Add(sub, Body(amount: 5m));

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(sub, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(sub, id)).StatusCode);

        Assert.Equal([kept], factory.Store.Entries(sub).Select(entry => entry.Id));
    }

    // M6 "Done when": edit and delete update totals everywhere. In M6 those are the Entries
    // total and the planned indicator (Add form and edit dialog), with the M5 hand-check numbers.
    [Fact]
    public async Task Edit_and_delete_update_the_totals()
    {
        var sub = await SetUp();
        var moved = await Add(sub, Body(date: "2026-10-01", amount: 400m));
        var deleted = await Add(sub, Body(date: "2026-10-17", amount: 799.99m));
        await AssertTotals(sub, groceriesSep: (1_199.99m, 300.01m), fuelOct: 0m, sepTotal: 1_199.99m, octTotal: 0m);

        // 400 moves from Groceries in Sep to Fuel on 28 Oct (Oct 2026).
        Assert.Equal(HttpStatusCode.OK, (await Put(sub, moved, Body("fuel", date: "2026-10-28", amount: 400m))).StatusCode);
        await AssertTotals(sub, groceriesSep: (799.99m, 700.01m), fuelOct: 400m, sepTotal: 799.99m, octTotal: 400m);

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(sub, deleted)).StatusCode);
        await AssertTotals(sub, groceriesSep: (0m, 1_500m), fuelOct: 400m, sepTotal: 0m, octTotal: 400m);

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(sub, moved)).StatusCode);
        await AssertTotals(sub, groceriesSep: (0m, 1_500m), fuelOct: 0m, sepTotal: 0m, octTotal: 0m);
    }

    private async Task AssertTotals(string sub, (decimal Spent, decimal Remaining) groceriesSep, decimal fuelOct, decimal sepTotal, decimal octTotal)
    {
        var groceries = await Json(await Send(factory, HttpMethod.Get, "/api/indicator?subCategoryId=groceries&date=2026-10-17", sub));
        Assert.Equal(groceriesSep, (groceries["spent"]!.GetValue<decimal>(), groceries["remaining"]!.GetValue<decimal>()));
        var fuel = await Json(await Send(factory, HttpMethod.Get, "/api/indicator?subCategoryId=fuel&date=2026-10-28", sub));
        Assert.Equal(fuelOct, fuel["spent"]!.GetValue<decimal>());
        var sep = await Json(await Send(factory, HttpMethod.Get, "/api/entries?periodKey=2026-09", sub));
        var oct = await Json(await Send(factory, HttpMethod.Get, "/api/entries?periodKey=2026-10", sub));
        Assert.Equal((sepTotal, octTotal), (sep["total"]!.GetValue<decimal>(), oct["total"]!.GetValue<decimal>()));
    }

    [Fact]
    public async Task Without_a_token_gets_401()
    {
        var client = factory.CreateClient();

        var put = await client.PutAsync("/api/entries/x", System.Net.Http.Json.JsonContent.Create(Body()));
        var delete = await client.DeleteAsync("/api/entries/x");

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (put.StatusCode, delete.StatusCode));
    }
}
