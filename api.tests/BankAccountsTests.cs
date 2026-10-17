using System.Net;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 7.5 GET /api/bank-accounts for the Add form (PRD 5.4). No DELETE (PRD 4.6).
public class BankAccountsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Sorted by name ignoring case (the default before any entry, M5); archived ones are flagged.
    [Fact]
    public async Task Get_returns_the_users_accounts_by_name_with_the_archived_flag()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddBankAccount(sub, new BankAccount("closed", "cash", Archived: true));
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
}
