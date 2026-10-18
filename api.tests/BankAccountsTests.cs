using System.Net;
using System.Text.Json.Nodes;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 7.5 bank accounts: the list for the Add form (PRD 5.4), and add, rename, archive and
// restore in Settings (STG-04, PRD 4.7). No DELETE (PRD 4.6).
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

    private Task<HttpResponseMessage> Add(string sub, string? name) =>
        Send(factory, HttpMethod.Post, "/api/bank-accounts", sub, new JsonObject { ["name"] = name });

    private Task<HttpResponseMessage> Update(string sub, string id, string? name, bool? archived) =>
        Send(factory, HttpMethod.Put, $"/api/bank-accounts/{id}", sub, new JsonObject { ["name"] = name, ["archived"] = archived });

    private static async Task AssertError(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.True(problem["errors"]!.AsObject().ContainsKey(field), problem.ToJsonString());
    }

    [Fact]
    public async Task Add_trims_the_name_and_refuses_a_duplicate_ignoring_case()
    {
        var sub = await SetUpUser(factory);

        var response = await Add(sub, "  Cash  ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await Json(response);
        Assert.Equal(("Cash", false), (account["name"]!.GetValue<string>(), account["archived"]!.GetValue<bool>()));
        await AssertError(await Add(sub, "snb"), "Name");
        await AssertError(await Add(sub, " "), "Name");
        await AssertError(await Add(sub, new string('x', 41)), "Name");
        Assert.Equal(3, (await factory.Store.GetBankAccountsAsync(sub)).Count);
    }

    [Fact]
    public async Task Rename_archive_and_restore()
    {
        var sub = await SetUpUser(factory);

        // Only the case changes: that is the same account, so it is allowed.
        var renamed = await Update(sub, "bank", "snb", false);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        await AssertError(await Update(sub, "bank", "AL RAJHI", false), "Name");

        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "bank", "SNB Gold", true)).StatusCode);
        Assert.Contains(new BankAccount("bank", "SNB Gold", Archived: true), await factory.Store.GetBankAccountsAsync(sub));

        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "bank", "SNB Gold", false)).StatusCode);
        Assert.Contains(new BankAccount("bank", "SNB Gold", Archived: false), await factory.Store.GetBankAccountsAsync(sub));
    }

    // Entries need an account (decision 5), so the last active one cannot be archived.
    [Fact]
    public async Task The_last_active_account_cannot_be_archived()
    {
        var sub = await SetUpUser(factory);
        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "bank", "SNB", true)).StatusCode);

        await AssertError(await Update(sub, "rajhi", "Al Rajhi", true), "Archived");
        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "rajhi", "Al Rajhi 2", false)).StatusCode);
        Assert.Contains(new BankAccount("rajhi", "Al Rajhi 2"), await factory.Store.GetBankAccountsAsync(sub));
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
