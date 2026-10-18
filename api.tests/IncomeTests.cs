using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 7.5 income (R-INC-01 to R-INC-03, STG-03). FY 2026 runs Jan to Dec 2026 with payday 27;
// today is 17 Oct 2026 in Riyadh, so the current month is Sep 2026.
public class IncomeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // The M4 hand check: salary 15,000 and rent income 2,000 every month, a once bonus of 500.25 in Sep.
    private static readonly IncomeItem[] Incomes =
    [
        new("s", IncomeKind.Salary, "Salary", 1_500_000, "2026-01", null),
        new("r", IncomeKind.Additional, "Rent", 200_000, "2026-01", null),
        new("b", IncomeKind.Additional, "Bonus", 50_025, "2026-09", "2026-09"),
    ];

    private async Task<JsonNode> Ok(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);
        return await Json(response);
    }

    private Task<HttpResponseMessage> Get(string sub, string query = "") =>
        Send(factory, HttpMethod.Get, "/api/income" + query, sub);

    private Task<HttpResponseMessage> ChangeSalary(string sub, decimal amount, string from) =>
        Send(factory, HttpMethod.Post, "/api/income/salary", sub,
            new JsonObject { ["amount"] = amount, ["effectiveFromPeriodKey"] = from });

    private static JsonObject Item(string label, decimal amount, string type, string from, string? to = null) => new()
    {
        ["label"] = label,
        ["amount"] = amount,
        ["type"] = type,
        ["fromPeriodKey"] = from,
        ["toPeriodKey"] = to,
    };

    private static decimal Dec(JsonNode? node) => node!.GetValue<decimal>();

    private static (string, decimal, string, string?)[] Rows(JsonNode income, string list) =>
        income[list]!.AsArray()
            .Select(item => (item!["label"]!.GetValue<string>(), Dec(item["amount"]), item["fromPeriodKey"]!.GetValue<string>(), item["toPeriodKey"]?.GetValue<string>()))
            .ToArray();

    private static decimal Total(JsonNode income, string periodKey) =>
        Dec(income["totals"]!.AsArray().Single(row => row!["periodKey"]!.GetValue<string>() == periodKey)!["total"]);

    private static void AssertError(JsonNode problem, string field) =>
        Assert.True(problem["errors"]!.AsObject().ContainsKey(field), problem.ToJsonString());

    // R-INC-01 and STG-03 "Total income for the current month" (the M4 hand check).
    [Fact]
    public async Task Get_returns_the_items_and_the_totals_of_the_current_fiscal_year()
    {
        var sub = await SetUpUser(factory, Incomes);

        var income = await Ok(await Get(sub));

        Assert.Equal("2026-01", income["fyId"]!.GetValue<string>());
        Assert.Single(income["fiscalYears"]!.AsArray());
        Assert.Equal([("Salary", 15_000m, "2026-01", (string?)null)], Rows(income, "salaries"));
        Assert.Equal([("Bonus", 500.25m, "2026-09", "2026-09"), ("Rent", 2_000m, "2026-01", null)], Rows(income, "additional"));
        Assert.Equal(["once", "monthly"], income["additional"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()));
        Assert.Equal(12, income["totals"]!.AsArray().Count);
        Assert.Equal(17_000m, Total(income, "2026-01"));
        Assert.Equal(17_500.25m, Total(income, "2026-09"));
        Assert.Equal("2026-09", income["currentPeriodKey"]!.GetValue<string>());
        Assert.Equal(17_500.25m, Dec(income["currentTotal"]));
    }

    [Fact]
    public async Task No_current_total_when_no_fiscal_year_contains_today()
    {
        var sub = await SetUpUser(factory, Incomes);
        factory.Time.UtcNow = new DateTimeOffset(2027, 2, 1, 9, 0, 0, TimeSpan.Zero);
        try
        {
            var income = await Ok(await Get(sub));

            Assert.Equal("2026-01", income["fyId"]!.GetValue<string>());
            Assert.Null(income["currentPeriodKey"]);
            Assert.Null(income["currentTotal"]);
        }
        finally
        {
            factory.Time.UtcNow = TestTime.Default;
        }
    }

    [Fact]
    public async Task An_unknown_fiscal_year_returns_404()
    {
        var sub = await SetUpUser(factory, Incomes);

        Assert.Equal(HttpStatusCode.NotFound, (await Get(sub, "?fyId=2030-01")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(sub, "?fyId=2026-01")).StatusCode);
    }

    // R-INC-02, the M2 example: 10,000 from Jan, 12,000 from Jun, then 11,000 from Mar gives
    // Jan to Feb 10,000 and Mar onward 11,000. The 12,000 item is replaced.
    [Fact]
    public async Task Salary_change_keeps_the_history()
    {
        var sub = await SetUpUser(factory, [new("s1", IncomeKind.Salary, "Salary", 1_000_000, "2026-01", null)]);

        var afterJune = await Ok(await ChangeSalary(sub, 12_000m, "2026-06"));
        Assert.Equal([("Salary", 12_000m, "2026-06", (string?)null), ("Salary", 10_000m, "2026-01", "2026-05")], Rows(afterJune, "salaries"));

        var income = await Ok(await ChangeSalary(sub, 11_000m, "2026-03"));

        Assert.Equal([("Salary", 11_000m, "2026-03", (string?)null), ("Salary", 10_000m, "2026-01", "2026-02")], Rows(income, "salaries"));
        Assert.Equal(10_000m, Total(income, "2026-02"));
        Assert.Equal(11_000m, Total(income, "2026-03"));
        Assert.Equal(11_000m, Total(income, "2026-12"));
        // The ended item keeps its id (createdAt is kept in Firestore).
        Assert.Contains(await factory.Store.GetIncomesAsync(sub), item => item.Id == "s1" && item.ToPeriodKey == "2026-02");
        Assert.Equal(2, (await factory.Store.GetIncomesAsync(sub)).Count);
    }

    [Fact]
    public async Task A_salary_of_0_is_allowed()
    {
        var sub = await SetUpUser(factory, Incomes);

        var income = await Ok(await ChangeSalary(sub, 0m, "2026-09"));

        Assert.Equal(2_500.25m, Total(income, "2026-09"));
        Assert.Equal(17_000m, Total(income, "2026-08"));
    }

    [Theory]
    [InlineData("2027-01", "EffectiveFromPeriodKey")]
    [InlineData("2025-12", "EffectiveFromPeriodKey")]
    [InlineData("2026-13", "EffectiveFromPeriodKey")]
    public async Task Salary_from_a_month_outside_every_fiscal_year_returns_400(string from, string field)
    {
        var sub = await SetUpUser(factory, Incomes);

        AssertError(await Ok(await ChangeSalary(sub, 16_000m, from), HttpStatusCode.BadRequest), field);
        Assert.Equal(3, (await factory.Store.GetIncomesAsync(sub)).Count);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("10000000.01")]
    [InlineData("1.005")]
    public async Task Invalid_salary_amounts_return_400(string amount)
    {
        var sub = await SetUpUser(factory, Incomes);

        AssertError(await Ok(await ChangeSalary(sub, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "2026-09"), HttpStatusCode.BadRequest), "Amount");
    }

    // R-INC-03: Once is one month; Monthly runs from a month, optionally until a month.
    [Fact]
    public async Task Additional_items_can_be_added_edited_and_deleted()
    {
        var sub = await SetUpUser(factory, Incomes);

        var added = await Ok(await Send(factory, HttpMethod.Post, "/api/income/additional", sub,
            Item("  Freelance  ", 750.50m, "monthly", "2026-10", "2026-11")), HttpStatusCode.Created);
        Assert.Equal(17_750.50m, Total(added, "2026-10"));
        var id = added["additional"]!.AsArray().Single(item => item!["label"]!.GetValue<string>() == "Freelance")!["id"]!.GetValue<string>();
        Assert.Equal(17_000m, Total(added, "2026-12"));

        // Once: the until month is ignored and stored equal to from.
        var edited = await Ok(await Send(factory, HttpMethod.Put, $"/api/income/additional/{id}", sub,
            Item("Freelance", 300m, "once", "2026-12", "2027-01")));
        Assert.Contains(("Freelance", 300m, "2026-12", (string?)"2026-12"), Rows(edited, "additional"));
        Assert.Equal(17_000m, Total(edited, "2026-10"));
        Assert.Equal(17_300m, Total(edited, "2026-12"));

        // Monthly without until is open ended.
        var openEnded = await Ok(await Send(factory, HttpMethod.Put, $"/api/income/additional/{id}", sub,
            Item("Freelance", 300m, "monthly", "2026-11")));
        Assert.Contains(("Freelance", 300m, "2026-11", (string?)null), Rows(openEnded, "additional"));

        var deleted = await Ok(await Send(factory, HttpMethod.Delete, $"/api/income/additional/{id}", sub));
        Assert.DoesNotContain(deleted["additional"]!.AsArray(), item => item!["id"]!.GetValue<string>() == id);
        Assert.Equal(17_000m, Total(deleted, "2026-12"));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Delete, $"/api/income/additional/{id}", sub)).StatusCode);
    }

    public static TheoryData<JsonObject, string> InvalidItems => new()
    {
        { Item("", 100m, "monthly", "2026-01"), "Label" },
        { Item(new string('x', 41), 100m, "monthly", "2026-01"), "Label" },
        { Item("Gift", 0m, "once", "2026-01"), "Amount" },
        { Item("Gift", 1.005m, "once", "2026-01"), "Amount" },
        { Item("Gift", 100m, "weekly", "2026-01"), "Type" },
        { Item("Gift", 100m, "monthly", "2026-05", "2026-04"), "ToPeriodKey" },
        { Item("Gift", 100m, "monthly", "2027-01"), "FromPeriodKey" },
        { Item("Gift", 100m, "monthly", "2026-01", "2027-03"), "ToPeriodKey" },
    };

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task Invalid_additional_items_return_400(JsonObject body, string field)
    {
        var sub = await SetUpUser(factory, Incomes);

        AssertError(await Ok(await Send(factory, HttpMethod.Post, "/api/income/additional", sub, body), HttpStatusCode.BadRequest), field);
        AssertError(await Ok(await Send(factory, HttpMethod.Put, "/api/income/additional/r", sub, body.DeepClone()), HttpStatusCode.BadRequest), field);
        Assert.Equal(Incomes, await factory.Store.GetIncomesAsync(sub));
    }

    // The salary changes only through POST /api/income/salary (R-INC-02).
    [Fact]
    public async Task A_salary_item_unknown_item_or_other_users_item_returns_404()
    {
        var sub = await SetUpUser(factory, Incomes);
        var other = await SetUpUser(factory);
        var body = Item("Gift", 100m, "once", "2026-09");

        foreach (var (user, id) in new[] { (sub, "s"), (sub, "missing"), (other, "r") })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Put, $"/api/income/additional/{id}", user, body.DeepClone())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Delete, $"/api/income/additional/{id}", user)).StatusCode);
        }
        Assert.Equal(Incomes, await factory.Store.GetIncomesAsync(sub));
    }
}
