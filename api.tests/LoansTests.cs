using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.12 Loans (owner note 20): GET /api/loans, /api/loans/totals, POST, PUT, settled, DELETE.
// Today is 17 Oct 2026 in Riyadh; FY 2026 runs 27 Jan 2026 to 26 Jan 2027 with payday 27.
public class LoansTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly FyStart = new(2026, 1, 27);

    private static JsonObject Loan(string name = "Ahmed", string type = "lent", decimal amount = 1000, string date = "2026-10-10",
        string bankAccountId = "bank", string? note = null)
    {
        var body = new JsonObject { ["name"] = name, ["type"] = type, ["amount"] = amount, ["date"] = date, ["bankAccountId"] = bankAccountId };
        if (note is not null)
        {
            body["note"] = note;
        }
        return body;
    }

    // SNB 5,000 and Al Rajhi 20,000 at the FY's start, salary 15,000 split to SNB, a Groceries plan and
    // an entry, so every balance and projection has numbers.
    private async Task<string> SetUpWithMoney()
    {
        var sub = await SetUpUser(factory, [new("salary", IncomeKind.Salary, "Salary", 1_500_000, "2026-01", null)], accounts:
        [
            new BankAccount("bank", "SNB", Opening: new OpeningBalance(500_000, FyStart)),
            new BankAccount("rajhi", "Al Rajhi", Opening: new OpeningBalance(2_000_000, FyStart)),
        ]);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.SetPlanAsync(sub, "2026-01", new PlanAmounts("groceries", "food",
            new Dictionary<string, long> { ["2026-09"] = 150_000, ["2026-10"] = 150_000 }));
        await factory.Store.AddEntryAsync(sub, new Entry("e1", new DateOnly(2026, 10, 17), "2026-09", "2026-01", "food", "groceries", "bank", 12_550, ""));
        return sub;
    }

    private async Task<JsonNode> Post(string sub, JsonObject body)
    {
        var response = await Send(factory, HttpMethod.Post, "/api/loans", sub, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private static string Id(JsonNode loans, string name) =>
        loans["loans"]!.AsArray().First(loan => loan!["name"]!.GetValue<string>() == name)!["id"]!.GetValue<string>();

    // Everything NAMA shows outside the Loans page.
    private async Task<string> EverythingElse(string sub)
    {
        var parts = new List<string>();
        foreach (var url in new[] { "/api/summary", "/api/banks", "/api/fiscal-years/2026-01/overview", "/api/fiscal-years/2026-01/balances",
                     "/api/fiscal-years/2026-01/months/2026-09", "/api/fiscal-years/2026-01/plans", "/api/income?fyId=2026-01", "/api/entries?fyId=2026-01" })
        {
            var response = await Send(factory, HttpMethod.Get, url, sub);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            parts.Add(await response.Content.ReadAsStringAsync());
        }
        return string.Join("\n", parts);
    }

    // R-LON-02: information only. Adding, settling, editing and deleting loans changes no other number.
    [Fact]
    public async Task Loans_change_no_balance_plan_or_tracker_number()
    {
        var sub = await SetUpWithMoney();
        var before = await EverythingElse(sub);

        var added = await Post(sub, Loan());
        await Post(sub, Loan(name: "Sara", type: "borrowed", amount: 3000, date: "2026-11-05", bankAccountId: "rajhi"));
        Assert.Equal(before, await EverythingElse(sub));

        var id = Id(added, "Ahmed");
        Assert.Equal(HttpStatusCode.OK, (await Send(factory, HttpMethod.Put, $"/api/loans/{id}/settled", sub, new JsonObject { ["settledOn"] = "2026-10-15" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(factory, HttpMethod.Put, $"/api/loans/{id}", sub, Loan(amount: 2500))).StatusCode);
        Assert.Equal(before, await EverythingElse(sub));

        Assert.Equal(HttpStatusCode.OK, (await Send(factory, HttpMethod.Delete, $"/api/loans/{id}", sub)).StatusCode);
        Assert.Equal(before, await EverythingElse(sub));
    }

    // LON-02, LON-03, R-LON-04: the page's one call.
    [Fact]
    public async Task Loans_page_has_the_loans_totals_people_accounts_and_month_ends()
    {
        var sub = await SetUpWithMoney();
        await Post(sub, Loan(name: "Ahmed", amount: 1000, date: "2026-09-01"));
        await Post(sub, Loan(name: " ahmed ", amount: 500.25m, date: "2026-10-10", note: "  Rent help "));
        await Post(sub, Loan(name: "Sara", type: "borrowed", amount: 3000, date: "2025-06-01", bankAccountId: "rajhi"));

        var page = await Json(await Send(factory, HttpMethod.Get, "/api/loans", sub));

        Assert.Equal("2026-10-17", page["today"]!.GetValue<string>());
        Assert.Equal(
            """{"id":"x","name":"ahmed","type":"lent","amount":500.25,"date":"2026-10-10","bankAccountId":"bank","note":"Rent help","settledOn":null}""",
            Without(page["loans"]![0]!, "id"));
        Assert.Equal(["2026-10-10", "2026-09-01", "2025-06-01"], page["loans"]!.AsArray().Select(loan => loan!["date"]!.GetValue<string>()));
        Assert.Equal(
            """{"lent":{"open":1500.25,"openCount":2,"settled":0,"settledCount":0},"borrowed":{"open":3000,"openCount":1,"settled":0,"settledCount":0},"net":-1499.75}""",
            page["totals"]!.ToJsonString());
        Assert.Equal(
            """[{"name":"ahmed","lentOpen":1500.25,"borrowedOpen":0,"openCount":2},{"name":"Sara","lentOpen":0,"borrowedOpen":3000,"openCount":1}]""",
            page["people"]!.ToJsonString());
        // R-BAL-03 today, as Banks shows it.
        var banks = await Json(await Send(factory, HttpMethod.Get, "/api/banks", sub));
        Assert.Equal(
            banks["accounts"]!.AsArray().Select(account => (account!["id"]!.GetValue<string>(), account["balance"]!["balance"]!.GetValue<decimal>())),
            page["accounts"]!.AsArray().Select(account => (account!["id"]!.GetValue<string>(), account["balance"]!.GetValue<decimal>())));
        Assert.True(page["complete"]!.GetValue<bool>());
        // The month ends are Tracker's Balance row, with each month named (R-CAL-11).
        var balances = await Json(await Send(factory, HttpMethod.Get, "/api/fiscal-years/2026-01/balances", sub));
        var months = page["months"]!.AsArray();
        Assert.Equal(12, months.Count);
        Assert.Equal(
            balances["months"]!.AsArray().Select(month => (month!["periodKey"]!.GetValue<string>(), month["balance"]?.GetValue<decimal>(), month["projected"]!.GetValue<bool>())),
            months.Select(month => (month!["period"]!["key"]!.GetValue<string>(), month["balance"]?.GetValue<decimal>(), month["projected"]!.GetValue<bool>())));
        Assert.Equal(("2026-10", "Nov 2026", true), (months[9]!["period"]!["key"]!.GetValue<string>(),
            months[9]!["period"]!["label"]!.GetValue<string>(), months[9]!["projected"]!.GetValue<bool>()));
    }

    private static string Without(JsonNode node, string field)
    {
        var copy = node.DeepClone().AsObject();
        copy[field] = "x";
        return copy.ToJsonString();
    }

    // DSH-07: the totals, and no names at all.
    [Fact]
    public async Task Totals_have_no_names()
    {
        var sub = await SetUpUser(factory);
        await Post(sub, Loan(name: "Ahmed"));

        var response = await Send(factory, HttpMethod.Get, "/api/loans/totals", sub);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(
            """{"lent":{"open":1000,"openCount":1,"settled":0,"settledCount":0},"borrowed":{"open":0,"openCount":0,"settled":0,"settledCount":0},"net":1000}""",
            text);
        Assert.DoesNotContain("Ahmed", text);
    }

    // R-LON-01: any date counts, outside every FY too.
    [Theory]
    [InlineData("2020-01-01")]
    [InlineData("2031-12-31")]
    public async Task Any_date_counts(string date)
    {
        var sub = await SetUpUser(factory);

        var page = await Post(sub, Loan(date: date));

        Assert.Equal(1000m, page["totals"]!["lent"]!["open"]!.GetValue<decimal>());
    }

    public static TheoryData<string, Action<JsonObject>, string> InvalidLoans => new()
    {
        { "no name", b => b["name"] = "  ", "Name" },
        { "name over 40", b => b["name"] = new string('a', 41), "Name" },
        { "unknown type", b => b["type"] = "gift", "Type" },
        { "no type", b => b.Remove("type"), "Type" },
        { "amount 0", b => b["amount"] = 0, "Amount" },
        { "amount with 3 decimals", b => b["amount"] = 1.005m, "Amount" },
        { "no amount", b => b.Remove("amount"), "Amount" },
        { "no date", b => b.Remove("date"), "Date" },
        { "unknown account", b => b["bankAccountId"] = "nope", "BankAccountId" },
        { "archived account", b => b["bankAccountId"] = "old", "BankAccountId" },
        { "note over 200", b => b["note"] = new string('n', 201), "Note" },
    };

    [Theory]
    [MemberData(nameof(InvalidLoans))]
    public async Task Invalid_loan_returns_400_and_saves_nothing(string reason, Action<JsonObject> change, string field)
    {
        _ = reason;
        var sub = await SetUpUser(factory, accounts: [new BankAccount("bank", "SNB"), new BankAccount("old", "Old", Archived: true)]);
        var body = Loan();
        change(body);

        var response = await Send(factory, HttpMethod.Post, "/api/loans", sub, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response))["errors"]!.AsObject().ContainsKey(field));
        Assert.Empty(await factory.Store.GetLoansAsync(sub));
    }

    // R-LON-03: returned in full on or after its date, and open again.
    [Fact]
    public async Task Settling_moves_a_loan_to_settled_and_back()
    {
        var sub = await SetUpUser(factory);
        var id = Id(await Post(sub, Loan()), "Ahmed");
        var url = $"/api/loans/{id}/settled";

        var early = await Send(factory, HttpMethod.Put, url, sub, new JsonObject { ["settledOn"] = "2026-10-09" });
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);
        Assert.True((await Json(early))["errors"]!.AsObject().ContainsKey("SettledOn"));

        var settled = await Json(await Send(factory, HttpMethod.Put, url, sub, new JsonObject { ["settledOn"] = "2026-10-10" }));
        Assert.Equal("2026-10-10", settled["loans"]![0]!["settledOn"]!.GetValue<string>());
        Assert.Equal((0m, 1000m, 0m), (settled["totals"]!["lent"]!["open"]!.GetValue<decimal>(),
            settled["totals"]!["lent"]!["settled"]!.GetValue<decimal>(), settled["totals"]!["net"]!.GetValue<decimal>()));
        Assert.Empty(settled["people"]!.AsArray());

        var open = await Json(await Send(factory, HttpMethod.Put, url, sub, new JsonObject { ["settledOn"] = null }));
        Assert.Null(open["loans"]![0]!["settledOn"]);
        Assert.Equal(1000m, open["totals"]!["lent"]!["open"]!.GetValue<decimal>());
    }

    // An edit keeps the settled date and cannot move the loan after it; an archived account the loan
    // already names stays allowed.
    [Fact]
    public async Task Edit_keeps_the_settled_date()
    {
        var sub = await SetUpUser(factory);
        var id = Id(await Post(sub, Loan()), "Ahmed");
        await Send(factory, HttpMethod.Put, $"/api/loans/{id}/settled", sub, new JsonObject { ["settledOn"] = "2026-10-12" });
        await factory.Store.UpdateBankAccountAsync(sub, new BankAccount("bank", "SNB", Archived: true));

        var edited = await Send(factory, HttpMethod.Put, $"/api/loans/{id}", sub, Loan(name: "Ahmed K", amount: 750));

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(
            new Loan(id, "Ahmed K", LoanType.Lent, 75_000, new DateOnly(2026, 10, 10), "bank", "", new DateOnly(2026, 10, 12)),
            await factory.Store.GetLoanAsync(sub, id));
        var late = await Send(factory, HttpMethod.Put, $"/api/loans/{id}", sub, Loan(date: "2026-10-13"));
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        Assert.True((await Json(late))["errors"]!.AsObject().ContainsKey("Date"));
    }

    // Another user's loan, or an unknown one, is 404 everywhere.
    [Fact]
    public async Task Unknown_or_other_users_loan_is_404()
    {
        var owner = await SetUpUser(factory);
        var id = Id(await Post(owner, Loan()), "Ahmed");
        var other = await SetUpUser(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Put, $"/api/loans/{id}", other, Loan())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Put, $"/api/loans/{id}/settled", other, new JsonObject { ["settledOn"] = null })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Delete, $"/api/loans/{id}", other)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Delete, "/api/loans/nope", owner)).StatusCode);
        Assert.Single(await factory.Store.GetLoansAsync(owner));
        Assert.Empty((await Json(await Send(factory, HttpMethod.Get, "/api/loans", other)))["loans"]!.AsArray());
    }

    // Before setup there is nothing to show.
    [Fact]
    public async Task Loans_before_setup_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Get, "/api/loans", NewSub())).StatusCode);
    }
}
