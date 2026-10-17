using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.3 Plan: GET and PUT /api/fiscal-years/{fyId}/plans (PLN-01, PLN-03, PLN-04, R-PLN-01 to R-PLN-03).
// Today is 17 Oct 2026 in Riyadh; FY 2026 runs Jan to Dec 2026 with payday 27.
public class PlansTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Fy = "2026-01";

    // The hand check of the M4 "Done when": salary 15,000 and rent income 2,000 every month,
    // a once bonus of 500.25 in Sep.
    private static readonly IncomeItem[] Incomes =
    [
        new("s", IncomeKind.Salary, "Salary", 1_500_000, "2026-01", null),
        new("r", IncomeKind.Additional, "Rent", 200_000, "2026-01", null),
        new("b", IncomeKind.Additional, "Bonus", 50_025, "2026-09", "2026-09"),
    ];

    private async Task<string> SetUp(IReadOnlyList<IncomeItem>? incomes = null, FiscalYear? fiscalYear = null)
    {
        var sub = await SetUpUser(factory, incomes, fiscalYear);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("fuel", "transport", "Fuel", 0));
        return sub;
    }

    private Task<HttpResponseMessage> Put(string sub, string subCategoryId, JsonObject amounts, string fyId = Fy) =>
        Send(factory, HttpMethod.Put, $"/api/fiscal-years/{fyId}/plans/{subCategoryId}", sub, new JsonObject { ["amounts"] = amounts });

    private async Task<JsonNode> Get(string sub)
    {
        var response = await Send(factory, HttpMethod.Get, $"/api/fiscal-years/{Fy}/plans", sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private async Task<IReadOnlyDictionary<string, long>> Stored(string sub, string subCategoryId) =>
        (await factory.Store.GetPlansAsync(sub, Fy)).Single(plan => plan.SubCategoryId == subCategoryId).Amounts;

    private static JsonObject EveryMonth(decimal amount) =>
        new(Enumerable.Range(1, 12).Select(m => KeyValuePair.Create<string, JsonNode?>($"2026-{m:00}", amount)));

    // PLN-04, R-STA-02: Jan 2026 is closed on 17 Oct 2026 and still takes amounts.
    [Fact]
    public async Task Amounts_save_in_minor_units_and_come_back_including_closed_months()
    {
        var sub = await SetUp();

        var response = await Put(sub, "groceries", new JsonObject { ["2026-01"] = 1500, ["2026-10"] = 1500.5m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, long> { ["2026-01"] = 150_000, ["2026-10"] = 150_050 }, await Stored(sub, "groceries"));

        var plan = (await Get(sub))["plans"]!.AsArray().Single()!;
        Assert.Equal(("groceries", "food"), (plan["subCategoryId"]!.GetValue<string>(), plan["categoryId"]!.GetValue<string>()));
        Assert.Equal(1500m, plan["amounts"]!["2026-01"]!.GetValue<decimal>());
        Assert.Equal(1500.5m, plan["amounts"]!["2026-10"]!.GetValue<decimal>());
        Assert.Equal(3000.5m, plan["total"]!.GetValue<decimal>());
    }

    // Owner's choice (M4): 0 and blank (null or left out) clear a month; a PUT replaces all months.
    [Fact]
    public async Task Zero_null_and_missing_months_are_cleared()
    {
        var sub = await SetUp();
        await Put(sub, "groceries", EveryMonth(1500));

        var response = await Put(sub, "groceries", new JsonObject { ["2026-01"] = 0, ["2026-02"] = null, ["2026-03"] = 200 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, long> { ["2026-03"] = 20_000 }, await Stored(sub, "groceries"));
    }

    // R-MON-02 (0 allowed to clear), and only the FY's own months (R-CAL-09).
    [Theory]
    [InlineData("2026-01", "1.005", "Amounts[2026-01]")]
    [InlineData("2026-01", "-1", "Amounts[2026-01]")]
    [InlineData("2026-01", "10000000.01", "Amounts[2026-01]")]
    [InlineData("2027-01", "100", "Amounts")]
    [InlineData("2025-12", "100", "Amounts")]
    public async Task Invalid_amounts_return_400_and_change_nothing(string key, string amount, string field)
    {
        var sub = await SetUp();
        await Put(sub, "groceries", new JsonObject { ["2026-05"] = 100 });

        var response = await Put(sub, "groceries", new JsonObject { [key] = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.True(problem["errors"]!.AsObject().ContainsKey(field), problem.ToJsonString());
        Assert.Equal(new Dictionary<string, long> { ["2026-05"] = 10_000 }, await Stored(sub, "groceries"));
    }

    [Fact]
    public async Task A_body_without_amounts_returns_400()
    {
        var sub = await SetUp();
        var response = await Send(factory, HttpMethod.Put, $"/api/fiscal-years/{Fy}/plans/groceries", sub, new JsonObject());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // AUTH-03: another user's sub-category is unknown to this user.
    [Fact]
    public async Task Unknown_fiscal_year_or_sub_category_returns_404()
    {
        var sub = await SetUp();
        var other = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(other, new SubCategory("theirs", "food", "Theirs", 0));

        Assert.Equal(HttpStatusCode.NotFound, (await Put(sub, "groceries", EveryMonth(1), fyId: "2027-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(sub, "nope", EveryMonth(1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(sub, "theirs", EveryMonth(1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Get, "/api/fiscal-years/2027-01/plans", sub)).StatusCode);
        Assert.Empty(await factory.Store.GetPlansAsync(other, Fy));
    }

    // PLN-03, R-PLN-02, R-PLN-03, R-INC-01: the totals row equals the hand check.
    // Groceries (Food) 1,500 every month, Fuel (Transport) 400 from Sep.
    [Fact]
    public async Task Totals_row_matches_the_hand_check()
    {
        var sub = await SetUp(Incomes);
        await Put(sub, "groceries", EveryMonth(1500));
        var response = await Put(sub, "fuel", new JsonObject { ["2026-09"] = 400, ["2026-10"] = 400, ["2026-11"] = 400, ["2026-12"] = 400 });

        // The PUT answers with the same body as the GET, so Plan needs no second call.
        var body = await Json(response);
        Assert.Equal((await Get(sub)).ToJsonString(), body.ToJsonString());

        var totals = body["totals"]!.AsArray()
            .Select(t => (t!["periodKey"]!.GetValue<string>(), t["planned"]!.GetValue<decimal>(),
                t["income"]!.GetValue<decimal>(), t["unplanned"]!.GetValue<decimal>()))
            .ToList();
        Assert.Equal(12, totals.Count);
        // Jan: 1,500 planned; 15,000 + 2,000 = 17,000 income; 17,000 - 1,500 = 15,500.
        Assert.Equal(("2026-01", 1500m, 17000m, 15500m), totals[0]);
        // Sep: 1,500 + 400 = 1,900 planned; 17,000 + 500.25 = 17,500.25 income; 15,600.25 unplanned.
        Assert.Equal(("2026-09", 1900m, 17500.25m, 15600.25m), totals[8]);
        Assert.Equal(("2026-12", 1900m, 17000m, 15100m), totals[11]);

        // R-PLN-02: FY totals per category: 12 × 1,500 and 4 × 400.
        Assert.Equal(18000m, body["categoryTotals"]!["food"]!.GetValue<decimal>());
        Assert.Equal(1600m, body["categoryTotals"]!["transport"]!.GetValue<decimal>());
    }

    // R-PLN-03: unplanned income is negative when more is planned than earned.
    [Fact]
    public async Task Unplanned_is_negative_when_planned_exceeds_income()
    {
        var sub = await SetUp();

        var body = await Json(await Put(sub, "groceries", new JsonObject { ["2026-02"] = 250.75m }));

        var february = body["totals"]![1]!;
        Assert.Equal((250.75m, 0m, -250.75m),
            (february["planned"]!.GetValue<decimal>(), february["income"]!.GetValue<decimal>(), february["unplanned"]!.GetValue<decimal>()));
    }

    // R-CAL-09: the months are the stored ones, never recomputed. This FY's first month was
    // stored with a different start (as after a payday change, R-CAL-08).
    [Fact]
    public async Task Months_come_from_the_stored_fiscal_year()
    {
        var computed = FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 10, 17), 1, 27);
        var periods = computed.Periods.ToList();
        periods[0] = periods[0] with { StartDate = new DateOnly(2026, 1, 20) };
        var sub = await SetUp(fiscalYear: computed with { StartDate = new DateOnly(2026, 1, 20), Periods = periods });

        var fiscalYear = (await Get(sub))["fiscalYear"]!;

        Assert.Equal(("2026-01", "FY 2026", "2026-01-20"),
            (fiscalYear["id"]!.GetValue<string>(), fiscalYear["label"]!.GetValue<string>(), fiscalYear["startDate"]!.GetValue<string>()));
        Assert.Equal(12, fiscalYear["periods"]!.AsArray().Count);
        Assert.Equal("""{"key":"2026-01","label":"Jan 2026","startDate":"2026-01-20","endDate":"2026-02-26"}""",
            fiscalYear["periods"]![0]!.ToJsonString());
    }

    [Fact]
    public async Task Fiscal_years_are_listed_with_their_months()
    {
        var sub = await SetUp();

        var response = await Send(factory, HttpMethod.Get, "/api/fiscal-years", sub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await Json(response)).AsArray();
        var fy = Assert.Single(list)!;
        Assert.Equal(("2026-01", "FY 2026", "2026-01-27", "2027-01-26", false),
            (fy["id"]!.GetValue<string>(), fy["label"]!.GetValue<string>(), fy["startDate"]!.GetValue<string>(),
                fy["endDate"]!.GetValue<string>(), fy["isTransition"]!.GetValue<bool>()));
        Assert.Equal("Dec 2026", fy["periods"]![11]!["label"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("GET", "/api/fiscal-years")]
    [InlineData("GET", "/api/fiscal-years/2026-01/plans")]
    [InlineData("PUT", "/api/fiscal-years/2026-01/plans/groceries")]
    public async Task Calls_without_a_token_return_401(string method, string url)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
