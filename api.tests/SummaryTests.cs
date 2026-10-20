using System.Net;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// UX-08 GET /api/summary: the top bar's month and balance. Today is 17 Oct 2026 in Riyadh; FY 2026
// has payday 27, so Sep 2026 (27 Sep to 26 Oct) is in progress with 10 days left, today included.
public class SummaryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Fy = "2026-01";

    // Salary 17,000; Groceries 1,500 every month; 50 spent in Aug and 1,199.99 in Sep. Jan to Aug leave
    // 7 × 1,500 + 1,450 = 11,950, so Sep has 11,950 + 1,500 - 1,199.99 = 12,250.01 left. Unassigned
    // carries 9 months of 15,500: 139,500.
    private async Task<string> SetUp(IReadOnlyList<BankAccount>? accounts = null)
    {
        var sub = await SetUpUser(factory, [new("s", IncomeKind.Salary, "Salary", 1_700_000, Fy, null)], accounts: accounts);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.SetPlanAsync(sub, Fy, new PlanAmounts("groceries", "food",
            FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 10, 17), 1, 27).Periods.ToDictionary(period => period.Key, _ => 150_000L)));
        await AddEntry(sub, new DateOnly(2026, 9, 10), "2026-08", 5_000);
        await AddEntry(sub, new DateOnly(2026, 10, 1), "2026-09", 119_999);
        return sub;
    }

    private Task AddEntry(string sub, DateOnly date, string periodKey, long amountMinor) =>
        factory.Store.AddEntryAsync(sub, new Entry(DocumentIds.New(), date, periodKey, Fy, "food", "groceries", "bank", amountMinor, ""));

    private async Task<System.Text.Json.Nodes.JsonNode> Get(string sub, DateTimeOffset? utcNow = null)
    {
        factory.Time.UtcNow = utcNow ?? TestTime.Default;
        var response = await Send(factory, HttpMethod.Get, "/api/summary", sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static decimal Dec(System.Text.Json.Nodes.JsonNode? node) => node!.GetValue<decimal>();

    [Fact]
    public async Task The_month_matches_the_hand_check()
    {
        var month = (await Get(await SetUp()))["month"]!;

        Assert.Equal(("2026-09", 10), (month["period"]!["key"]!.GetValue<string>(), month["daysLeft"]!.GetValue<int>()));
        // DSH-01: 17 Oct is day 21 of the 30 days from 27 Sep to 26 Oct.
        Assert.Equal((21, 30), (month["day"]!.GetValue<int>(), month["days"]!.GetValue<int>()));
        Assert.Equal((17_000m, 11_950m, 1_500m, 1_199.99m, 12_250.01m, 139_500m),
            (Dec(month["income"]), Dec(month["carriedIn"]), Dec(month["planned"]), Dec(month["spent"]), Dec(month["remaining"]),
                Dec(month["unassigned"])));
    }

    // R-BAL-03: SNB opened with 1,000 on 1 Oct 2026 and gets all income; the Sep salary arrived on
    // 27 Sep, before it, so it is already in the 1,000. 1,199.99 was spent from SNB on 1 Oct.
    [Fact]
    public async Task The_balance_is_the_total_of_the_active_accounts()
    {
        var sub = await SetUp([
            new BankAccount("bank", "SNB", Opening: new OpeningBalance(100_000, new DateOnly(2026, 10, 1))),
            new BankAccount("rajhi", "Al Rajhi", Opening: new OpeningBalance(50_000, new DateOnly(2026, 10, 1))),
        ]);

        var summary = await Get(sub);

        Assert.True(summary["complete"]!.GetValue<bool>());
        Assert.Equal(1_000m - 1_199.99m + 500m, Dec(summary["balance"]));
        Assert.Equal([("rajhi", 500m), ("bank", -199.99m)],
            summary["accounts"]!.AsArray().Select(account => (account!["id"]!.GetValue<string>(), Dec(account["balance"]))));
    }

    // Until every active account has an opening balance, the top bar asks for it instead of a total.
    [Fact]
    public async Task Without_every_opening_balance_there_is_no_total()
    {
        var sub = await SetUp([
            new BankAccount("bank", "SNB", Opening: new OpeningBalance(100_000, new DateOnly(2026, 10, 1))),
            new BankAccount("rajhi", "Al Rajhi"),
        ]);

        var summary = await Get(sub);

        Assert.False(summary["complete"]!.GetValue<bool>());
        Assert.Null(summary["balance"]);
        Assert.Null(summary["accounts"]![0]!["balance"]);
    }

    // After the FY ends and before the next one is started, no month is in progress.
    [Fact]
    public async Task No_month_after_the_last_fiscal_year()
    {
        var summary = await Get(await SetUp(), new DateTimeOffset(2027, 1, 27, 9, 0, 0, TimeSpan.Zero));

        Assert.Null(summary["month"]);
    }

    // R-PLN-07, R-PLN-08 with the hand-check data: Sep has 12,250.01 left to spend; Oct, Nov and Dec
    // each leave 17,000 − 1,500 = 15,500 (46,500). Saved to date 9 × 17,000 − 1,249.99 = 151,750.01, so
    // the projected saving is 151,750.01 − 12,250.01 + 46,500 = 186,000, the planned saved (12 × 15,500).
    // The balance (1,000 − 1,199.99 + 500 = 300.01, see above) ends at 300.01 − 12,250.01 + 46,500 = 34,550.
    [Fact]
    public async Task The_projection_matches_the_hand_check()
    {
        var sub = await SetUp([
            new BankAccount("bank", "SNB", Opening: new OpeningBalance(100_000, new DateOnly(2026, 10, 1))),
            new BankAccount("rajhi", "Al Rajhi", Opening: new OpeningBalance(50_000, new DateOnly(2026, 10, 1))),
        ]);

        var projection = (await Get(sub))["projection"]!;

        Assert.Equal(("FY 2026", "2027-01-26"), (projection["fiscalYearLabel"]!.GetValue<string>(), projection["endDate"]!.GetValue<string>()));
        Assert.Equal((300.01m, 12_250.01m, 46_500m, 34_550m),
            (Dec(projection["balanceToday"]), Dec(projection["stillToSpend"]), Dec(projection["futureUnallocated"]),
                Dec(projection["projectedBalance"])));
        Assert.Equal([("2026-10", 15_500m), ("2026-11", 15_500m), ("2026-12", 15_500m)],
            projection["futureMonths"]!.AsArray().Select(month => (month!["period"]!["key"]!.GetValue<string>(), Dec(month["unallocated"]))));
        Assert.Equal((151_750.01m, 186_000m, 186_000m),
            (Dec(projection["savedToDate"]), Dec(projection["plannedSaved"]), Dec(projection["projectedSaving"])));
    }

    // Until the balance is complete, the projected saving is known but the projected balance is not.
    [Fact]
    public async Task Without_a_complete_balance_there_is_no_projected_balance()
    {
        var sub = await SetUp([
            new BankAccount("bank", "SNB", Opening: new OpeningBalance(100_000, new DateOnly(2026, 10, 1))),
            new BankAccount("rajhi", "Al Rajhi"),
        ]);

        var projection = (await Get(sub))["projection"]!;

        Assert.Null(projection["balanceToday"]);
        Assert.Null(projection["projectedBalance"]);
        Assert.Equal(186_000m, Dec(projection["projectedSaving"]));
    }

    [Fact]
    public async Task No_projection_after_the_last_fiscal_year()
    {
        var summary = await Get(await SetUp(), new DateTimeOffset(2027, 1, 27, 9, 0, 0, TimeSpan.Zero));

        Assert.Null(summary["projection"]);
    }

    // One entries read serves the balances and the month (the top bar loads after every change).
    [Fact]
    public async Task Entries_are_read_once()
    {
        var sub = await SetUp([new BankAccount("bank", "SNB", Opening: new OpeningBalance(0, new DateOnly(2026, 3, 1)))]);
        var before = factory.Store.EntriesFromReads;

        await Get(sub);

        Assert.Equal(before + 1, factory.Store.EntriesFromReads);
    }

    [Fact]
    public async Task Without_a_token_gets_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/summary")).StatusCode);
    }
}
