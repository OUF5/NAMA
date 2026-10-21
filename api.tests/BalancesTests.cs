using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// Owner note 9: GET /api/fiscal-years/{fyId}/balances, Tracker's Year view Balance row. Today is
// 17 Oct 2026 in Riyadh, so Sep 2026 (27 Sep to 26 Oct) is in progress.
public class BalancesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Fy = "2026-01";

    // Salary 17,000, Groceries 1,500 every month. SNB opened with 1,000 and Al Rajhi with 500 on 27 Aug
    // (Aug's payday), all income goes to SNB. 50 spent on 10 Sep (Aug), 1,199.99 on 1 Oct (Sep).
    private async Task<string> SetUp(IReadOnlyList<BankAccount>? accounts = null)
    {
        var opened = new DateOnly(2026, 8, 27);
        var sub = await SetUpUser(factory, [new("s", IncomeKind.Salary, "Salary", 1_700_000, Fy, null)], accounts: accounts ??
        [
            new BankAccount("bank", "SNB", Opening: new OpeningBalance(100_000, opened)),
            new BankAccount("rajhi", "Al Rajhi", Opening: new OpeningBalance(50_000, opened)),
        ]);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.SetPlanAsync(sub, Fy, new PlanAmounts("groceries", "food",
            FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 10, 17), 1, 27).Periods.ToDictionary(period => period.Key, _ => 150_000L)));
        await AddEntry(sub, new DateOnly(2026, 9, 10), "2026-08", 5_000);
        await AddEntry(sub, new DateOnly(2026, 10, 1), "2026-09", 119_999);
        return sub;
    }

    private Task AddEntry(string sub, DateOnly date, string periodKey, long amountMinor) =>
        factory.Store.AddEntryAsync(sub, new Entry(DocumentIds.New(), date, periodKey, Fy, "food", "groceries", "bank", amountMinor, ""));

    private async Task<JsonNode> GetOk(string sub, DateTimeOffset? utcNow = null)
    {
        factory.Time.UtcNow = utcNow ?? TestTime.Default;
        var response = await Send(factory, HttpMethod.Get, $"/api/fiscal-years/{Fy}/balances", sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static IEnumerable<(string, decimal?, bool)> Months(JsonNode balances) => balances["months"]!.AsArray().Select(month => (
        month!["periodKey"]!.GetValue<string>(), month["balance"]?.GetValue<decimal>(), month["projected"]!.GetValue<bool>()));

    // Jan to Jul ended before the accounts were opened: unknown. Aug ended on 26 Sep with
    // 1,000 + 500 + 17,000 − 50 = 18,450. Today: 18,450 + 17,000 − 1,199.99 = 34,250.01, with 12,250.01
    // of Sep's plan left (11,950 carried + 1,500 − 1,199.99), so Sep ends at 22,000; Oct to Dec add
    // 15,500 each: 37,500, 53,000 and 68,500, the projected balance (R-PLN-07).
    [Fact]
    public async Task The_balances_match_the_hand_check()
    {
        var balances = await GetOk(await SetUp());

        Assert.True(balances["complete"]!.GetValue<bool>());
        var months = Months(balances).ToList();
        Assert.All(months.Take(7), month => Assert.Equal((null, false), (month.Item2, month.Item3)));
        Assert.Equal(
            [("2026-08", 18_450m, false), ("2026-09", 22_000m, true), ("2026-10", 37_500m, true), ("2026-11", 53_000m, true), ("2026-12", 68_500m, true)],
            months.Skip(7));
    }

    // The last month's projected end is the Dashboard's projected balance.
    [Fact]
    public async Task The_last_month_is_the_projected_balance()
    {
        var sub = await SetUp();
        var balances = await GetOk(sub);
        var summary = await Json(await Send(factory, HttpMethod.Get, "/api/summary", sub));

        Assert.Equal(summary["projection"]!["projectedBalance"]!.GetValue<decimal>(), Months(balances).Last().Item2);
    }

    [Fact]
    public async Task Without_a_complete_balance_every_month_is_unknown()
    {
        var balances = await GetOk(await SetUp(
        [
            new BankAccount("bank", "SNB", Opening: new OpeningBalance(100_000, new DateOnly(2026, 8, 27))),
            new BankAccount("rajhi", "Al Rajhi"),
        ]));

        Assert.False(balances["complete"]!.GetValue<bool>());
        Assert.All(Months(balances), month => Assert.Null(month.Item2));
    }

    // After the FY ends, every month is a real balance.
    [Fact]
    public async Task After_the_fiscal_year_every_month_is_real()
    {
        var balances = await GetOk(await SetUp(), new DateTimeOffset(2027, 1, 27, 9, 0, 0, TimeSpan.Zero));

        Assert.All(Months(balances), month => Assert.False(month.Item3));
        // Dec ends on 26 Jan 2027: 34,250.01 + Oct, Nov and Dec's income (3 × 17,000).
        Assert.Equal(85_250.01m, Months(balances).Last().Item2);
    }

    [Fact]
    public async Task An_unknown_fiscal_year_gets_404()
    {
        var response = await Send(factory, HttpMethod.Get, "/api/fiscal-years/2030-01/balances", await SetUpUser(factory));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_gets_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/fiscal-years/2026-01/balances")).StatusCode);
    }
}
