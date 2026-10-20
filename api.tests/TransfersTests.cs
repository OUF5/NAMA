using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// R-TRF-01: POST, PUT and DELETE /api/transfers (TRF-01, BNK-04). Today is 17 Oct 2026 in Riyadh;
// FY 2026 has payday 27, so 18 Oct 2026 is in Sep 2026 and 28 Oct 2026 in Oct 2026.
public class TransfersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static JsonObject Body(string from = "bank", string to = "rajhi", string date = "2026-10-18", decimal? amount = 500.25m,
        string? note = "  Savings ") => new()
    {
        ["fromBankAccountId"] = from,
        ["toBankAccountId"] = to,
        ["date"] = date,
        ["amount"] = amount,
        ["note"] = note,
    };

    private Task<HttpResponseMessage> Post(string sub, JsonObject body) => Send(factory, HttpMethod.Post, "/api/transfers", sub, body);

    private async Task<string> Created(string sub, JsonObject? body = null)
    {
        var response = await Post(sub, body ?? Body());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response))["id"]!.GetValue<string>();
    }

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string field)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await Json(response);
        Assert.True(problem["errors"]!.AsObject().ContainsKey(field), problem.ToJsonString());
    }

    // The API sets the month and FY from the date (R-CAL-09) and trims the note.
    [Fact]
    public async Task A_transfer_is_saved_with_its_month()
    {
        var sub = await SetUpUser(factory);

        var id = await Created(sub);

        var stored = (await factory.Store.GetTransfersAsync(sub)).Single();
        Assert.Equal(new Transfer(id, new DateOnly(2026, 10, 18), "2026-09", "2026-01", "bank", "rajhi", 50_025, "Savings"), stored);
    }

    // It only moves money between accounts: never spending, so Tracker, Entries and Dashboard are unchanged.
    [Fact]
    public async Task A_transfer_is_never_spending()
    {
        var sub = await SetUpUser(factory);
        await Created(sub);

        factory.Time.UtcNow = TestTime.Default;
        var overview = await Json(await Send(factory, HttpMethod.Get, "/api/fiscal-years/2026-01/overview", sub));
        var entries = await Json(await Send(factory, HttpMethod.Get, "/api/entries", sub));

        Assert.Equal(0m, overview["totals"]!["spent"]!.GetValue<decimal>());
        Assert.Equal(0m, overview["toDate"]!["spent"]!.GetValue<decimal>());
        Assert.Empty(entries["entries"]!.AsArray());
    }

    public static TheoryData<string, JsonObject, HttpStatusCode, string> InvalidBodies => new()
    {
        { "same account", Body(to: "bank"), HttpStatusCode.BadRequest, "ToBankAccountId" },
        { "unknown from", Body(from: "nope"), HttpStatusCode.BadRequest, "FromBankAccountId" },
        { "unknown to", Body(to: "nope"), HttpStatusCode.BadRequest, "ToBankAccountId" },
        { "amount 0", Body(amount: 0), HttpStatusCode.BadRequest, "Amount" },
        { "amount with 3 decimals", Body(amount: 1.005m), HttpStatusCode.BadRequest, "Amount" },
        { "no amount", Body(amount: null), HttpStatusCode.BadRequest, "Amount" },
        { "note over 200", Body(note: new string('x', 201)), HttpStatusCode.BadRequest, "Note" },
        { "before the first FY", Body(date: "2026-01-26"), HttpStatusCode.UnprocessableEntity, "Date" },
        { "after the last FY", Body(date: "2027-01-27"), HttpStatusCode.UnprocessableEntity, "Date" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task An_invalid_transfer_is_refused_and_nothing_is_saved(string reason, JsonObject body, HttpStatusCode status, string field)
    {
        _ = reason;
        var sub = await SetUpUser(factory);

        await AssertError(await Post(sub, body), status, field);
        Assert.Empty(await factory.Store.GetTransfersAsync(sub));
    }

    // Pickers hide archived accounts (PRD 4.6), so the API refuses them, except on the transfer
    // that already has one.
    [Fact]
    public async Task An_archived_account_only_stays_on_a_transfer_that_has_it()
    {
        var sub = await SetUpUser(factory);
        var id = await Created(sub);
        await factory.Store.UpdateBankAccountAsync(sub, new BankAccount("rajhi", "Al Rajhi", Archived: true));

        await AssertError(await Post(sub, Body()), HttpStatusCode.BadRequest, "ToBankAccountId");

        var edited = await Send(factory, HttpMethod.Put, $"/api/transfers/{id}", sub, Body(amount: 600));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
    }

    // The date moves the transfer to its month; the accounts and the amount change too.
    [Fact]
    public async Task Edit_changes_every_field_and_the_month()
    {
        var sub = await SetUpUser(factory);
        var id = await Created(sub);

        var response = await Send(factory, HttpMethod.Put, $"/api/transfers/{id}", sub,
            Body(from: "rajhi", to: "bank", date: "2026-10-28", amount: 75, note: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-10", (await Json(response))["periodKey"]!.GetValue<string>());
        Assert.Equal(new Transfer(id, new DateOnly(2026, 10, 28), "2026-10", "2026-01", "rajhi", "bank", 7_500, ""),
            (await factory.Store.GetTransfersAsync(sub)).Single());
    }

    [Fact]
    public async Task Delete_removes_it_once()
    {
        var sub = await SetUpUser(factory);
        var id = await Created(sub);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(factory, HttpMethod.Delete, $"/api/transfers/{id}", sub)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Delete, $"/api/transfers/{id}", sub)).StatusCode);
        Assert.Empty(await factory.Store.GetTransfersAsync(sub));
    }

    // NFR-03: another user's transfer is unknown.
    [Fact]
    public async Task Another_users_transfer_is_404()
    {
        var owner = await SetUpUser(factory);
        var id = await Created(owner);
        var other = await SetUpUser(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Put, $"/api/transfers/{id}", other, Body())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Delete, $"/api/transfers/{id}", other)).StatusCode);
        Assert.Single(await factory.Store.GetTransfersAsync(owner));
    }

    // Banks lists them newest first; same-date ones added later first.
    [Fact]
    public async Task Banks_lists_transfers_newest_first()
    {
        var sub = await SetUpUser(factory);
        var first = await Created(sub, Body(date: "2026-10-01"));
        var second = await Created(sub, Body(date: "2026-10-18"));
        var third = await Created(sub, Body(date: "2026-10-01"));

        factory.Time.UtcNow = TestTime.Default;
        var banks = await Json(await Send(factory, HttpMethod.Get, "/api/banks", sub));

        Assert.Equal([second, third, first], banks["transfers"]!.AsArray().Select(transfer => transfer!["id"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("POST", "/api/transfers")]
    [InlineData("PUT", "/api/transfers/x")]
    [InlineData("DELETE", "/api/transfers/x")]
    public async Task Without_a_token_gets_401(string method, string url)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
