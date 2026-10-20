using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 7.5 bank accounts: the list for the Add form (PRD 5.4), and add, rename, set the opening
// balance, archive and restore on Banks (BNK-02, R-BAL-01, PRD 4.7). No DELETE (PRD 4.6). Changes
// answer with GET /api/banks.
public class BankAccountsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Sorted by name ignoring case (the default before any entry, M5); archived ones are flagged.
    [Fact]
    public async Task Get_returns_the_users_accounts_by_name_with_the_archived_flag()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddBankAccountAsync(sub, new BankAccount("closed", "cash", Archived: true));
        await SetUpUser(factory);

        var response = await Send(factory, HttpMethod.Get, "/api/bank-accounts", sub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """[{"id":"rajhi","name":"Al Rajhi","archived":false},{"id":"closed","name":"cash","archived":true},{"id":"bank","name":"SNB","archived":false}]""",
            (await Json(response)).ToJsonString());
    }

    [Fact]
    public async Task Without_a_token_gets_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/bank-accounts")).StatusCode);
    }

    private Task<HttpResponseMessage> Add(string sub, string? name, decimal? openingBalance = 0, string? openingDate = "2026-10-17") =>
        Send(factory, HttpMethod.Post, "/api/bank-accounts", sub,
            new JsonObject { ["name"] = name, ["openingBalance"] = openingBalance, ["openingDate"] = openingDate });

    private Task<HttpResponseMessage> Update(string sub, string id, string? name, bool? archived, decimal? openingBalance = null,
        string? openingDate = null) =>
        Send(factory, HttpMethod.Put, $"/api/bank-accounts/{id}", sub, new JsonObject
        {
            ["name"] = name,
            ["archived"] = archived,
            ["openingBalance"] = openingBalance,
            ["openingDate"] = openingDate,
        });

    // Moves every share of income to one account (R-BAL-02).
    private async Task AllIncomeTo(string sub, string id)
    {
        var response = await Send(factory, HttpMethod.Put, "/api/income-split", sub,
            new JsonObject { ["effectiveFromPeriodKey"] = "2026-01", ["shares"] = new JsonObject { [id] = 100 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertError(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.True(problem["errors"]!.AsObject().ContainsKey(field), problem.ToJsonString());
    }

    // R-BAL-01: a new account starts with its balance at the start of a day of a stored FY.
    [Fact]
    public async Task Add_trims_the_name_keeps_the_opening_balance_and_refuses_a_duplicate_ignoring_case()
    {
        var sub = await SetUpUser(factory);

        var response = await Add(sub, "  Cash  ", 250.5m, "2026-10-17");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cash = (await factory.Store.GetBankAccountsAsync(sub)).Single(account => account.Name == "Cash");
        Assert.Equal(new OpeningBalance(25_050, new DateOnly(2026, 10, 17)), cash.Opening);
        // The answer is GET /api/banks, so the page shows the new balance at once.
        var listed = (await Json(response))["accounts"]!.AsArray().Single(account => account!["name"]!.GetValue<string>() == "Cash")!;
        Assert.Equal((250.5m, "2026-10-17", 250.5m), (listed["openingBalance"]!.GetValue<decimal>(),
            listed["openingDate"]!.GetValue<string>(), listed["balance"]!["balance"]!.GetValue<decimal>()));
        await AssertError(await Add(sub, "snb"), "Name");
        await AssertError(await Add(sub, " "), "Name");
        await AssertError(await Add(sub, new string('x', 41)), "Name");
        Assert.Equal(3, (await factory.Store.GetBankAccountsAsync(sub)).Count);
    }

    // R-BAL-01: the balance and its date come together; the date is inside a stored FY (R-STA-03).
    [Theory]
    [InlineData(null, "2026-10-17", "OpeningBalance")]
    [InlineData("100", null, "OpeningDate")]
    [InlineData("1.005", "2026-10-17", "OpeningBalance")]
    [InlineData("-10000000.01", "2026-10-17", "OpeningBalance")]
    [InlineData("100", "2026-01-26", "OpeningDate")]
    [InlineData("100", "2027-01-27", "OpeningDate")]
    public async Task Add_refuses_a_bad_opening_balance(string? balance, string? date, string field)
    {
        var sub = await SetUpUser(factory);
        decimal? amount = balance is null ? null : decimal.Parse(balance, System.Globalization.CultureInfo.InvariantCulture);

        await AssertError(await Add(sub, "Cash", amount, date), field);
        Assert.Equal(2, (await factory.Store.GetBankAccountsAsync(sub)).Count);
    }

    [Fact]
    public async Task Rename_set_the_opening_balance_archive_and_restore()
    {
        var sub = await SetUpUser(factory);

        // Only the case changes: that is the same account, so it is allowed.
        var renamed = await Update(sub, "rajhi", "al rajhi", false);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        await AssertError(await Update(sub, "rajhi", "SNB", false), "Name");

        // A negative balance is fine (a card); the opening balance is set on an account made before it existed.
        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "rajhi", "Al Rajhi", false, -300, "2026-09-01")).StatusCode);
        Assert.Contains(new BankAccount("rajhi", "Al Rajhi", Opening: new OpeningBalance(-30_000, new DateOnly(2026, 9, 1))),
            await factory.Store.GetBankAccountsAsync(sub));

        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "rajhi", "Al Rajhi", true, -300, "2026-09-01")).StatusCode);
        Assert.True((await factory.Store.GetBankAccountsAsync(sub)).Single(account => account.Id == "rajhi").Archived);

        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "rajhi", "Al Rajhi", false, -300, "2026-09-01")).StatusCode);
        Assert.False((await factory.Store.GetBankAccountsAsync(sub)).Single(account => account.Id == "rajhi").Archived);
    }

    // Entries need an account (decision 5), so the last active one cannot be archived.
    [Fact]
    public async Task The_last_active_account_cannot_be_archived()
    {
        var sub = await SetUpUser(factory);
        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "rajhi", "Al Rajhi", true)).StatusCode);
        await AllIncomeTo(sub, "bank");

        await AssertError(await Update(sub, "bank", "SNB", true), "Archived");
        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "bank", "SNB 2", false)).StatusCode);
        Assert.Contains(new BankAccount("bank", "SNB 2"), await factory.Store.GetBankAccountsAsync(sub));
    }

    // R-BAL-02: an account that gets income from now on cannot be archived, or the income would go
    // into a hidden account. Once its share moves, it can.
    [Fact]
    public async Task An_account_with_a_share_of_income_cannot_be_archived()
    {
        var sub = await SetUpUser(factory);

        await AssertError(await Update(sub, "bank", "SNB", true), "Archived");

        await AllIncomeTo(sub, "rajhi");
        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "bank", "SNB", true)).StatusCode);
    }

    [Fact]
    public async Task Update_without_archived_returns_400()
    {
        var sub = await SetUpUser(factory);

        await AssertError(await Update(sub, "bank", "SNB", null), "Archived");
    }

    [Fact]
    public async Task Unknown_or_other_users_account_returns_404()
    {
        var sub = await SetUpUser(factory);
        var other = await SetUpUser(factory);
        await factory.Store.AddBankAccountAsync(other, new BankAccount("cash", "Cash"));

        Assert.Equal(HttpStatusCode.NotFound, (await Update(sub, "cash", "Cash", false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Update(sub, "missing", "Cash", false)).StatusCode);
    }

    // PRD 4.6: there is no DELETE.
    [Fact]
    public async Task Delete_is_not_an_endpoint()
    {
        var sub = await SetUpUser(factory);

        var response = await Send(factory, HttpMethod.Delete, "/api/bank-accounts/bank", sub);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(2, (await factory.Store.GetBankAccountsAsync(sub)).Count);
    }

    [Theory]
    [InlineData("POST", "/api/bank-accounts")]
    [InlineData("PUT", "/api/bank-accounts/bank")]
    public async Task Changes_without_a_token_get_401(string method, string url)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
