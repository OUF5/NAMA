using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.11 Banks: GET /api/banks (BNK-01 to BNK-04) and PUT /api/income-split (R-BAL-01 to R-BAL-03).
// FY 2026 runs Jan to Dec 2026 with payday 27, so Jan 2026 is 27 Jan to 26 Feb 2026.
public class BanksTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Fy = "2026-01";

    // The last day of Jan 2026, 12:00 in Riyadh.
    private static readonly DateTimeOffset EndOfJanuary = new(2026, 2, 26, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly FyStart = new(2026, 1, 27);

    // The owner's example: Main 5,000 and Savings 20,000 at the start of the FY, a salary of 17,000,
    // split 80 / 20 (13,600 and 3,400). In Jan 2026, 14,000 is spent from Main and 500 moved to Savings.
    private async Task<string> SetUpExample()
    {
        factory.Time.UtcNow = EndOfJanuary;
        var sub = await SetUpUser(factory,
            incomes: [new("s", IncomeKind.Salary, "Salary", 1_700_000, Fy, null)],
            accounts: [
                new BankAccount("main", "Main", Opening: new OpeningBalance(500_000, FyStart)),
                new BankAccount("savings", "Savings", Opening: new OpeningBalance(2_000_000, FyStart)),
            ]);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        Assert.Equal(HttpStatusCode.OK, (await PutSplit(sub, Fy, new JsonObject { ["main"] = 80, ["savings"] = 20 })).StatusCode);
        await AddEntry(sub, new DateOnly(2026, 2, 1), 1_000_000);
        await AddEntry(sub, new DateOnly(2026, 2, 20), 400_000);
        await Move(sub, "main", "savings", "2026-02-10", 500);
        return sub;
    }

    private Task AddEntry(string sub, DateOnly date, long amountMinor, string account = "main") =>
        factory.Store.AddEntryAsync(sub, new Entry(DocumentIds.New(), date, "2026-01", Fy, "food", "groceries", account, amountMinor, ""));

    private async Task Move(string sub, string from, string to, string date, decimal amount)
    {
        var response = await Send(factory, HttpMethod.Post, "/api/transfers", sub, new JsonObject
        {
            ["fromBankAccountId"] = from,
            ["toBankAccountId"] = to,
            ["date"] = date,
            ["amount"] = amount,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task<HttpResponseMessage> PutSplit(string sub, string from, JsonObject shares) =>
        Send(factory, HttpMethod.Put, "/api/income-split", sub, new JsonObject { ["effectiveFromPeriodKey"] = from, ["shares"] = shares });

    private async Task<JsonNode> GetOk(string path, string sub, DateTimeOffset? utcNow = null)
    {
        factory.Time.UtcNow = utcNow ?? EndOfJanuary;
        var response = await Send(factory, HttpMethod.Get, path, sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static decimal Dec(JsonNode? node) => node!.GetValue<decimal>();

    private static JsonNode Account(JsonNode banks, string id) =>
        banks["accounts"]!.AsArray().Single(account => account!["id"]!.GetValue<string>() == id)!;

    private static (decimal, decimal, decimal, decimal, decimal, decimal) Breakdown(JsonNode account)
    {
        var balance = account["balance"]!;
        return (Dec(balance["opening"]), Dec(balance["income"]), Dec(balance["spent"]), Dec(balance["transfersIn"]),
            Dec(balance["transfersOut"]), Dec(balance["balance"]));
    }

    // R-BAL-03: Main 5,000 + 13,600 - 14,000 - 500 = 4,100; Savings 20,000 + 3,400 + 500 = 23,900.
    [Fact]
    public async Task Balances_match_the_worked_example()
    {
        var banks = await GetOk("/api/banks", await SetUpExample());

        Assert.Equal((5_000m, 13_600m, 14_000m, 0m, 500m, 4_100m), Breakdown(Account(banks, "main")));
        Assert.Equal((20_000m, 3_400m, 0m, 500m, 0m, 23_900m), Breakdown(Account(banks, "savings")));
        Assert.Equal(28_000m, Dec(banks["total"]));
        Assert.True(banks["complete"]!.GetValue<bool>());
        Assert.Equal((80m, 20m), (Dec(Account(banks, "main")["share"]), Dec(Account(banks, "savings")["share"])));
        Assert.Equal((13_600m, 3_400m), (Dec(Account(banks, "main")["monthIncome"]), Dec(Account(banks, "savings")["monthIncome"])));
        Assert.Equal(("2026-01", 80m), (banks["split"]!["fromPeriodKey"]!.GetValue<string>(), Dec(banks["split"]!["shares"]!["main"])));
        Assert.Equal(("2026-01-27", 5_000m),
            (Account(banks, "main")["openingDate"]!.GetValue<string>(), Dec(Account(banks, "main")["openingBalance"])));
        Assert.Equal("2026-01", banks["currentPeriodKey"]!.GetValue<string>());
        Assert.Equal(17_000m, Dec(banks["income"]!["2026-02"]));
        var transfer = banks["transfers"]!.AsArray().Single()!;
        Assert.Equal(("main", "savings", 500m, "2026-01"), (transfer["fromBankAccountId"]!.GetValue<string>(),
            transfer["toBankAccountId"]!.GetValue<string>(), Dec(transfer["amount"]), transfer["periodKey"]!.GetValue<string>()));
    }

    // The next salary arrives on 27 Feb 2026 (the first day of Feb 2026); nothing counts before it does.
    [Fact]
    public async Task Income_arrives_on_payday()
    {
        var banks = await GetOk("/api/banks", await SetUpExample(), new DateTimeOffset(2026, 2, 27, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal(4_100m + 13_600m, Dec(Account(banks, "main")["balance"]!["balance"]));
        Assert.Equal(28_000m + 17_000m, Dec(banks["total"]));
    }

    // R-BAL-03, R-INC-05 (owner note 17): a Both item of 600 into Savings in Feb 2026 arrives on 27 Feb,
    // that month's payday, with the salary; the split's amounts stay the salary's.
    [Fact]
    public async Task Income_into_one_account_arrives_there_on_payday()
    {
        var sub = await SetUpExample();
        await factory.Store.AddIncomeAsync(sub, new IncomeItem("refund", IncomeKind.Additional, "Refund", 60_000, "2026-02", "2026-02", BankAccountId: "savings"));
        await factory.Store.AddIncomeAsync(sub, new IncomeItem("bonus", IncomeKind.Additional, "Bonus", 100_000, "2026-02", "2026-02", IncomeCounts.Planned));

        var january = await GetOk("/api/banks", sub);
        Assert.Equal(28_000m, Dec(january["total"]));

        var banks = await GetOk("/api/banks", sub, new DateTimeOffset(2026, 2, 27, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal(23_900m + 3_400m + 600m, Dec(Account(banks, "savings")["balance"]!["balance"]));
        Assert.Equal(4_100m + 13_600m, Dec(Account(banks, "main")["balance"]!["balance"]));
        Assert.Equal((13_600m, 3_400m), (Dec(Account(banks, "main")["monthIncome"]), Dec(Account(banks, "savings")["monthIncome"])));
        Assert.Equal(17_000m, Dec(banks["income"]!["2026-02"]));
    }

    // The plan layer and the cash layer agree: on the last day of Jan 2026, the total balance is the
    // opening balances plus Unassigned (17,000 - 15,000 planned = 2,000) plus Remaining (15,000 - 14,000
    // spent = 1,000): 25,000 + 2,000 + 1,000 = 28,000. Transfers move money and change neither side.
    [Fact]
    public async Task The_bank_total_is_the_opening_balances_plus_unassigned_plus_remaining()
    {
        var sub = await SetUpExample();
        await factory.Store.SetPlanAsync(sub, Fy, new PlanAmounts("groceries", "food", new Dictionary<string, long> { [Fy] = 1_500_000 }));

        var summary = await GetOk("/api/summary", sub);
        var plans = await GetOk($"/api/fiscal-years/{Fy}/plans", sub);
        var overview = await GetOk($"/api/fiscal-years/{Fy}/overview", sub);

        var unassigned = Dec(plans["totals"]![0]!["unassigned"]);
        var remaining = Dec(overview["months"]![0]!["remaining"]);
        Assert.Equal((2_000m, 1_000m), (unassigned, remaining));
        Assert.Equal(25_000m + unassigned + remaining, Dec(summary["balance"]));
        Assert.Equal((2_000m, 1_000m), (Dec(summary["month"]!["unassigned"]), Dec(summary["month"]!["remaining"])));
    }

    // Accounts made before balances existed have none: Banks asks for them, and there is no total.
    [Fact]
    public async Task Without_opening_balances_there_is_no_balance()
    {
        var banks = await GetOk("/api/banks", await SetUpUser(factory));

        Assert.All(banks["accounts"]!.AsArray(), account =>
        {
            Assert.Null(account!["balance"]);
            Assert.Null(account["openingBalance"]);
            Assert.Null(account["openingDate"]);
        });
        Assert.Null(banks["total"]);
        Assert.False(banks["complete"]!.GetValue<bool>());
    }

    // An archived account keeps its balance on Banks, but the total covers the active accounts.
    [Fact]
    public async Task An_archived_account_is_not_in_the_total()
    {
        var sub = await SetUpExample();
        var savings = (await factory.Store.GetBankAccountsAsync(sub)).Single(account => account.Id == "savings");
        Assert.Equal(HttpStatusCode.OK, (await PutSplit(sub, Fy, new JsonObject { ["main"] = 100 })).StatusCode);
        await factory.Store.UpdateBankAccountAsync(sub, savings with { Archived = true });

        var banks = await GetOk("/api/banks", sub);

        Assert.Equal(4_100m + 3_400m, Dec(banks["total"]));
        Assert.Equal(20_000m + 500m, Dec(Account(banks, "savings")["balance"]!["balance"]));
    }

    // R-BAL-02: a new split applies from its month on and replaces the ones from that month on,
    // as the salary does (R-INC-02). Earlier months keep theirs.
    [Fact]
    public async Task A_new_split_replaces_the_ones_from_its_month_on()
    {
        var sub = await SetUpExample();

        Assert.Equal(HttpStatusCode.OK, (await PutSplit(sub, "2026-06", new JsonObject { ["main"] = 50, ["savings"] = 50 })).StatusCode);
        var response = await PutSplit(sub, "2026-04", new JsonObject { ["main"] = 66.67m, ["savings"] = 33.33m, ["unused"] = 0 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var banks = await Json(response);
        Assert.Equal(["2026-04", "2026-01"], banks["splitHistory"]!.AsArray().Select(split => split!["fromPeriodKey"]!.GetValue<string>()));
        Assert.Equal((66.67m, 33.33m), (Dec(banks["splitHistory"]![0]!["shares"]!["main"]), Dec(banks["splitHistory"]![0]!["shares"]!["savings"])));
        var stored = await factory.Store.GetIncomeSplitsAsync(sub);
        Assert.Equal(new Dictionary<string, int> { ["main"] = 6_667, ["savings"] = 3_333 },
            stored.Single(split => split.FromPeriodKey == "2026-04").Shares);
        // Jan 2026 is still split 80 / 20.
        Assert.Equal(4_100m, Dec(Account(banks, "main")["balance"]!["balance"]));
    }

    public static TheoryData<string, JsonObject, string> InvalidSplits => new()
    {
        { "2026-01", new JsonObject { ["main"] = 80, ["savings"] = 19.99m }, "Shares" },
        { "2026-01", new JsonObject { ["main"] = 100.01m, ["savings"] = -0.01m }, "Shares[main]" },
        { "2026-01", new JsonObject { ["main"] = 99.995m, ["savings"] = 0.005m }, "Shares[main]" },
        { "2026-01", new JsonObject { ["main"] = 50, ["savings"] = null }, "Shares[savings]" },
        { "2026-01", new JsonObject { ["main"] = 50, ["nope"] = 50 }, "Shares" },
        { "2025-12", new JsonObject { ["main"] = 100 }, "EffectiveFromPeriodKey" },
        { "2027-01", new JsonObject { ["main"] = 100 }, "EffectiveFromPeriodKey" },
    };

    // R-BAL-02: 0 to 100 with at most 2 decimals each, exactly 100 together, active accounts only,
    // and a stored month.
    [Theory]
    [MemberData(nameof(InvalidSplits))]
    public async Task An_invalid_split_returns_400_and_changes_nothing(string from, JsonObject shares, string field)
    {
        var sub = await SetUpExample();

        var response = await PutSplit(sub, from, shares);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.True(problem["errors"]!.AsObject().ContainsKey(field), problem.ToJsonString());
        Assert.Equal(["2026-01"], (await factory.Store.GetIncomeSplitsAsync(sub)).Select(split => split.FromPeriodKey));
    }

    [Fact]
    public async Task An_archived_account_cannot_get_a_share()
    {
        var sub = await SetUpExample();
        var savings = (await factory.Store.GetBankAccountsAsync(sub)).Single(account => account.Id == "savings");
        await factory.Store.UpdateBankAccountAsync(sub, savings with { Archived = true });

        var response = await PutSplit(sub, Fy, new JsonObject { ["main"] = 50, ["savings"] = 50 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Only the user's own data (NFR-03).
    [Fact]
    public async Task Another_users_accounts_are_never_listed()
    {
        await SetUpExample();
        var other = await SetUpUser(factory);

        var banks = await GetOk("/api/banks", other);

        Assert.Equal(["rajhi", "bank"], banks["accounts"]!.AsArray().Select(account => account!["id"]!.GetValue<string>()));
        Assert.Empty(banks["transfers"]!.AsArray());
    }

    [Theory]
    [InlineData("GET", "/api/banks")]
    [InlineData("PUT", "/api/income-split")]
    public async Task Without_a_token_gets_401(string method, string url)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
