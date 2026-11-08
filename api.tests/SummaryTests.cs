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
        Assert.Equal("2026-10-01", summary["accounts"]![0]!["openingDate"]!.GetValue<string>());
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
        // No start day: it counts from the FY's first month (R-PLN-06), so it names none.
        Assert.Null(projection["countedFrom"]);
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

    // Owner note 17 (R-INC-01, R-INC-04, R-PLN-07, R-PLN-08): a Planned bonus of 3,000 in Dec, an Actual
    // gift of 500 in Sep 2026, and an Actual 1,000 in Nov. The gift is in this month's income and saved
    // to date, not in Unassigned. The bonus is in the future months' unallocated and planned saved. The
    // Nov item is in neither until it comes.
    private static readonly IncomeItem[] Extras =
    [
        new("bonus", IncomeKind.Additional, "Bonus", 300_000, "2026-12", "2026-12", IncomeCounts.Planned),
        new("gift", IncomeKind.Additional, "Gift", 50_000, "2026-09", "2026-09", IncomeCounts.Actual),
        new("side", IncomeKind.Additional, "Side job", 100_000, "2026-11", "2026-11", IncomeCounts.Actual),
    ];

    [Fact]
    public async Task Planned_income_is_projected_and_actual_income_is_counted()
    {
        // Opened on 27 Sep, Sep's payday, so Sep's income (the gift too) is in the balance.
        var sub = await SetUp([
            new BankAccount("bank", "SNB", Opening: new OpeningBalance(100_000, new DateOnly(2026, 9, 27))),
            new BankAccount("rajhi", "Al Rajhi", Opening: new OpeningBalance(50_000, new DateOnly(2026, 9, 27))),
        ]);
        var before = await Get(sub);
        foreach (var item in Extras)
        {
            await factory.Store.AddIncomeAsync(sub, item);
        }

        var after = await Get(sub);

        decimal Change(string block, string field) => Dec(after[block]![field]) - Dec(before[block]![field]);
        Assert.Equal(500m, Dec(after["balance"]) - Dec(before["balance"]));
        Assert.Equal((500m, 0m), (Change("month", "income"), Change("month", "unassigned")));
        Assert.Equal((3_000m, 500m, 3_000m), (Change("projection", "futureUnallocated"), Change("projection", "savedToDate"), Change("projection", "plannedSaved")));
        Assert.Equal((3_500m, 3_500m), (Change("projection", "projectedBalance"), Change("projection", "projectedSaving")));
    }

    // Owner note 17: Plan shows the planned income; Tracker's months the actual income, with the planned
    // income next to it, and the month lists its additional income by name.
    [Fact]
    public async Task Plan_uses_planned_income_and_tracker_shows_actual_income()
    {
        var sub = await SetUp();
        foreach (var item in Extras)
        {
            await factory.Store.AddIncomeAsync(sub, item);
        }
        factory.Time.UtcNow = TestTime.Default;

        var plans = await Json(await Send(factory, HttpMethod.Get, $"/api/fiscal-years/{Fy}/plans", sub));
        decimal PlanIncome(string key) => Dec(plans["totals"]!.AsArray().Single(month => month!["periodKey"]!.GetValue<string>() == key)!["income"]);
        Assert.Equal((17_000m, 17_000m, 20_000m), (PlanIncome("2026-09"), PlanIncome("2026-11"), PlanIncome("2026-12")));

        var overview = await Json(await Send(factory, HttpMethod.Get, $"/api/fiscal-years/{Fy}/overview", sub));
        (decimal, decimal) Income(string key)
        {
            var month = overview["months"]!.AsArray().Single(month => month!["period"]!["key"]!.GetValue<string>() == key)!;
            return (Dec(month["income"]), Dec(month["plannedIncome"]));
        }
        Assert.Equal([(17_500m, 17_000m), (18_000m, 17_000m), (17_000m, 20_000m)], new[] { Income("2026-09"), Income("2026-11"), Income("2026-12") });
        Assert.Equal((205_500m, 207_000m), (Dec(overview["totals"]!["income"]), Dec(overview["totals"]!["plannedIncome"])));
        Assert.Equal(9 * 17_000m + 500m, Dec(overview["toDate"]!["income"]));

        var month = await Json(await Send(factory, HttpMethod.Get, $"/api/fiscal-years/{Fy}/months/2026-09", sub));
        var gift = Assert.Single(month["additionalIncome"]!.AsArray())!;
        Assert.Equal(("Gift", 500m, "actual"), (gift["label"]!.GetValue<string>(), Dec(gift["amount"]), gift["counts"]!.GetValue<string>()));
        var filtered = await Json(await Send(factory, HttpMethod.Get, $"/api/fiscal-years/{Fy}/months/2026-09?categoryId=food", sub));
        Assert.Empty(filtered["additionalIncome"]!.AsArray());
    }

    // ENT-05 (owner note 35): when each was added, a minute apart.
    private static DateTimeOffset Added(int minute) => new(2026, 10, 17, 9, minute, 0, TimeSpan.Zero);

    private Task AddExpense(string sub, string id, string subCategoryId, string account, DateOnly date, long amountMinor, int minute, string note = "") =>
        factory.Store.AddEntryAsync(sub, new Entry(id, date, "2026-09", Fy, "food", subCategoryId, account, amountMinor, note, Added(minute)));

    private Task AddTransfer(string sub, string id, string from, string to, DateOnly date, long amountMinor, int minute) =>
        factory.Store.AddTransferAsync(sub, new Transfer(id, date, "2026-09", Fy, from, to, amountMinor, "", Added(minute)));

    // Groceries 100 from SNB on 10 Oct, then Groceries 200 from Al Rajhi back-dated to 5 Oct, then Fuel
    // 300 from SNB on 1 Oct: each is the last added of its own key, whatever its date.
    [Fact]
    public async Task Last_expense_is_the_one_added_last_not_the_newest_date()
    {
        var sub = await SetUp();
        await AddExpense(sub, "e1", "groceries", "bank", new DateOnly(2026, 10, 10), 10_000, 1);
        await AddExpense(sub, "e2", "groceries", "rajhi", new DateOnly(2026, 10, 5), 20_000, 2, "Weekly shop");
        await AddExpense(sub, "e3", "fuel", "bank", new DateOnly(2026, 10, 1), 30_000, 3);

        var last = (await Get(sub))["lastAdded"]!;

        var groceries = last["expenseBySubCategory"]!["groceries"]!;
        Assert.Equal(("2026-10-05", "food", "groceries", "rajhi", 200m, "Weekly shop"),
            (groceries["date"]!.GetValue<string>(), groceries["categoryId"]!.GetValue<string>(), groceries["subCategoryId"]!.GetValue<string>(),
                groceries["bankAccountId"]!.GetValue<string>(), Dec(groceries["amount"]), groceries["note"]!.GetValue<string>()));
        Assert.Equal(300m, Dec(last["expenseBySubCategory"]!["fuel"]!["amount"]));
        Assert.Equal(300m, Dec(last["expenseByAccount"]!["bank"]!["amount"]));
        Assert.Equal(200m, Dec(last["expenseByAccount"]!["rajhi"]!["amount"]));
        Assert.Null(last["expenseByAccount"]!["other"]);
    }

    // An edit is not a new input (createdAt is kept), and the amount shown is the edited one. A deleted
    // entry leaves the one added before it.
    [Fact]
    public async Task Last_expense_stays_after_an_edit_and_steps_back_after_a_delete()
    {
        var sub = await SetUp();
        await AddExpense(sub, "e1", "fuel", "bank", new DateOnly(2026, 10, 10), 10_000, 1);
        await AddExpense(sub, "e2", "fuel", "bank", new DateOnly(2026, 10, 11), 20_000, 2);
        var stored = (await factory.Store.GetEntryAsync(sub, "e1"))!;
        await factory.Store.UpdateEntryAsync(sub, stored with { AmountMinor = 15_000, CreatedAt = default });
        var latest = (await factory.Store.GetEntryAsync(sub, "e2"))!;
        await factory.Store.UpdateEntryAsync(sub, latest with { AmountMinor = 25_000, CreatedAt = default });

        Assert.Equal(250m, Dec((await Get(sub))["lastAdded"]!["expenseBySubCategory"]!["fuel"]!["amount"]));

        await factory.Store.DeleteEntryAsync(sub, "e2");

        Assert.Equal(150m, Dec((await Get(sub))["lastAdded"]!["expenseBySubCategory"]!["fuel"]!["amount"]));
    }

    // SNB → Al Rajhi 500 on 15 Oct, then Al Rajhi → SNB 100 back-dated to 1 Oct.
    [Fact]
    public async Task Last_transfer_is_kept_for_each_account_and_direction()
    {
        var sub = await SetUp();
        await AddTransfer(sub, "t1", "bank", "rajhi", new DateOnly(2026, 10, 15), 50_000, 1);
        await AddTransfer(sub, "t2", "rajhi", "bank", new DateOnly(2026, 10, 1), 10_000, 2);

        var last = (await Get(sub))["lastAdded"]!;

        string Id(string map, string account) => last[map]![account]!["id"]!.GetValue<string>();
        Assert.Equal(("t1", "t2", "t2", "t1"), (Id("transferFrom", "bank"), Id("transferFrom", "rajhi"), Id("transferTo", "bank"), Id("transferTo", "rajhi")));
        var transfer = last["transferFrom"]!["bank"]!;
        Assert.Equal(("2026-10-15", "bank", "rajhi", 500m),
            (transfer["date"]!.GetValue<string>(), transfer["fromBankAccountId"]!.GetValue<string>(), transfer["toBankAccountId"]!.GetValue<string>(), Dec(transfer["amount"])));
    }

    // The salary is not additional income, however late it changed.
    [Fact]
    public async Task Last_income_is_the_additional_item_added_last()
    {
        var sub = await SetUp();
        await factory.Store.AddIncomeAsync(sub, new IncomeItem("gift", IncomeKind.Additional, "Gift", 50_000, "2026-09", "2026-09", IncomeCounts.Actual, "rajhi", Added(1)));
        await factory.Store.AddIncomeAsync(sub, new IncomeItem("bonus", IncomeKind.Additional, "Bonus", 30_000, "2026-10", null, IncomeCounts.Planned, null, Added(2)));
        await factory.Store.AddIncomeAsync(sub, new IncomeItem("raise", IncomeKind.Salary, "Salary", 1_800_000, "2026-11", null, CreatedAt: Added(3)));

        var income = (await Get(sub))["lastAdded"]!["income"]!;

        Assert.Equal(("Bonus", 300m, "2026-10"), (income["label"]!.GetValue<string>(), Dec(income["amount"]), income["fromPeriodKey"]!.GetValue<string>()));
        Assert.Null(income["toPeriodKey"]);
        Assert.Null(income["bankAccountId"]);
    }

    [Fact]
    public async Task Last_added_is_empty_before_anything_is_added()
    {
        var sub = await SetUpUser(factory, [new("s", IncomeKind.Salary, "Salary", 1_700_000, Fy, null)]);

        var last = (await Get(sub))["lastAdded"]!;

        Assert.Empty(last["expenseBySubCategory"]!.AsObject());
        Assert.Empty(last["expenseByAccount"]!.AsObject());
        Assert.Empty(last["transferFrom"]!.AsObject());
        Assert.Empty(last["transferTo"]!.AsObject());
        Assert.Null(last["income"]);
    }
}
