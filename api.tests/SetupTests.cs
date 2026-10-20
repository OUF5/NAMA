using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Api.Domain;

namespace Api.Tests;

// PRD 5.2: POST /api/setup (SET-01, SET-02, SET-04) and the live preview of step 2.
// Today is 17 Oct 2026 in Riyadh, the section 3 example.
public class SetupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static JsonObject ValidRequest() => new()
    {
        ["projectName"] = "  Home budget ",
        ["displayName"] = "Owner",
        ["currency"] = "SAR",
        ["timezone"] = "Asia/Riyadh",
        ["fiscalStartMonth"] = 1,
        ["paydayDay"] = 27,
        ["salary"] = 15000.50m,
        ["additionalIncome"] = new JsonArray(
            new JsonObject { ["label"] = "Rent", ["amount"] = 2000, ["type"] = "monthly", ["fromPeriodKey"] = "2026-01" },
            new JsonObject { ["label"] = "Bonus", ["amount"] = 500.25m, ["type"] = "once", ["fromPeriodKey"] = "2026-09" }),
        ["bankAccounts"] = new JsonArray("Al Rajhi", " SNB "),
        ["categories"] = new JsonArray("Food", "Housing", "Transport"),
    };

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string sub, JsonObject? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(sub: sub));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await factory.CreateClient().SendAsync(request);
    }

    private Task<HttpResponseMessage> PostSetup(string sub, JsonObject body) => Send(HttpMethod.Post, "/api/setup", sub, body);

    // A different user per test, so tests sharing the in-memory store don't meet.
    private static string NewSub() => "user-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Finish_saves_everything_in_one_go()
    {
        var sub = NewSub();
        var response = await PostSetup(sub, ValidRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = factory.Store.Saved(sub)!;

        Assert.Equal(new Api.Infrastructure.UserProfile(
            DisplayName: "Owner",
            Email: ApiFactory.AllowedEmail,
            ProjectName: "Home budget",
            Currency: "SAR",
            Timezone: "Asia/Riyadh",
            FiscalStartMonth: 1,
            PaydayDay: 27,
            SetupCompleted: true,
            LastBankAccountId: null), saved.Profile);

        // R-CAL-05: FY 2026 = 27 Jan 2026 to 26 Jan 2027, 12 months Jan 2026 to Dec 2026.
        var fy = saved.FiscalYear;
        Assert.Equal(("2026-01", "FY 2026", new DateOnly(2026, 1, 27), new DateOnly(2027, 1, 26), false),
            (fy.Id, fy.Label, fy.StartDate, fy.EndDate, fy.IsTransition));
        Assert.Equal(12, fy.Periods.Count);
        Assert.Equal("Jan 2026", fy.Periods[0].Label);
        Assert.Equal("Dec 2026", fy.Periods[^1].Label);

        // The salary runs from the FY's first month (owner's choice, M3); additional income uses the chosen months.
        Assert.Equal(
            [
                (IncomeKind.Salary, "Salary", 1_500_050L, "2026-01", (string?)null),
                (IncomeKind.Additional, "Rent", 200_000L, "2026-01", null),
                (IncomeKind.Additional, "Bonus", 50_025L, "2026-09", "2026-09"),
            ],
            saved.Incomes.Select(i => (i.Kind, i.Label, i.AmountMinor, i.FromPeriodKey, i.ToPeriodKey)));

        Assert.Equal(["Al Rajhi", "SNB"], saved.BankAccounts.Select(a => a.Name));
        Assert.Equal([("Food", 0), ("Housing", 1), ("Transport", 2)], saved.Categories.Select(c => (c.Name, c.SortOrder)));

        var me = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(me!["setupCompleted"]!.GetValue<bool>());
        Assert.Equal("2026-09", me["currentPeriodKey"]!.GetValue<string>());
    }

    // R-INC-03: a bonus that came earlier in the FY or comes later goes in its own month, and a
    // side income can start and end mid-year. Once ignores the until month.
    [Fact]
    public async Task Additional_income_goes_in_the_chosen_months()
    {
        var body = ValidRequest();
        body["additionalIncome"] = new JsonArray(
            new JsonObject { ["label"] = "Annual bonus", ["amount"] = 5000, ["type"] = "once", ["fromPeriodKey"] = "2026-03", ["toPeriodKey"] = "2026-05" },
            new JsonObject { ["label"] = "Eid bonus", ["amount"] = 1000, ["type"] = "once", ["fromPeriodKey"] = "2026-12" },
            new JsonObject { ["label"] = "Freelance", ["amount"] = 1500, ["type"] = "monthly", ["fromPeriodKey"] = "2026-06", ["toPeriodKey"] = "2026-11" },
            new JsonObject { ["label"] = "Rent", ["amount"] = 2000, ["type"] = "monthly", ["fromPeriodKey"] = "2026-10", ["toPeriodKey"] = "" });
        var sub = NewSub();

        Assert.Equal(HttpStatusCode.Created, (await PostSetup(sub, body)).StatusCode);
        Assert.Equal(
            [
                ("Annual bonus", "2026-03", (string?)"2026-03"),
                ("Eid bonus", "2026-12", "2026-12"),
                ("Freelance", "2026-06", "2026-11"),
                ("Rent", "2026-10", null),
            ],
            factory.Store.Saved(sub)!.Incomes.Skip(1).Select(i => (i.Label, i.FromPeriodKey, i.ToPeriodKey)));
    }

    [Fact]
    public async Task Salary_of_zero_is_allowed()
    {
        var body = ValidRequest();
        body["salary"] = 0;
        var sub = NewSub();

        Assert.Equal(HttpStatusCode.Created, (await PostSetup(sub, body)).StatusCode);
        Assert.Equal(0, factory.Store.Saved(sub)!.Incomes[0].AmountMinor);
    }

    [Fact]
    public async Task The_user_comes_from_the_token_never_from_the_body()
    {
        var body = ValidRequest();
        body["userId"] = "someone-else";
        body["email"] = "someone@else.com";
        var sub = NewSub();

        Assert.Equal(HttpStatusCode.Created, (await PostSetup(sub, body)).StatusCode);
        Assert.Null(factory.Store.Saved("someone-else"));
        Assert.Equal(ApiFactory.AllowedEmail, factory.Store.Saved(sub)!.Profile.Email);

        // Another user is still not set up.
        var other = await (await Send(HttpMethod.Get, "/api/me", NewSub())).Content.ReadFromJsonAsync<JsonObject>();
        Assert.False(other!["setupCompleted"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_second_setup_returns_409_and_changes_nothing()
    {
        var sub = NewSub();
        Assert.Equal(HttpStatusCode.Created, (await PostSetup(sub, ValidRequest())).StatusCode);
        var first = factory.Store.Saved(sub);

        var again = ValidRequest();
        again["projectName"] = "Other";
        var response = await PostSetup(sub, again);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Same(first, factory.Store.Saved(sub));
    }

    [Fact]
    public async Task Setup_without_a_token_returns_401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/setup", ValidRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static TheoryData<string, Action<JsonObject>, string> InvalidRequests => new()
    {
        { "no project name", b => b["projectName"] = "   ", "ProjectName" },
        { "project name over 40", b => b["projectName"] = new string('a', 41), "ProjectName" },
        { "no name", b => b.Remove("displayName"), "DisplayName" },
        { "currency not in the list", b => b["currency"] = "BHD", "Currency" },
        { "unknown time zone", b => b["timezone"] = "Mars/Olympus", "Timezone" },
        { "no time zone", b => b.Remove("timezone"), "Timezone" },
        { "start month 0", b => b["fiscalStartMonth"] = 0, "FiscalStartMonth" },
        { "start month 13", b => b["fiscalStartMonth"] = 13, "FiscalStartMonth" },
        { "payday 0", b => b["paydayDay"] = 0, "PaydayDay" },
        { "payday 32", b => b["paydayDay"] = 32, "PaydayDay" },
        { "no salary", b => b.Remove("salary"), "Salary" },
        { "negative salary", b => b["salary"] = -1, "Salary" },
        { "salary over 10,000,000", b => b["salary"] = 10_000_000.01m, "Salary" },
        { "salary with 3 decimals", b => b["salary"] = 1.005m, "Salary" },
        { "additional amount 0", b => b["additionalIncome"]![0]!["amount"] = 0, "AdditionalIncome[0].Amount" },
        { "additional amount missing", b => b["additionalIncome"]![1]!.AsObject().Remove("amount"), "AdditionalIncome[1].Amount" },
        { "additional without label", b => b["additionalIncome"]![0]!["label"] = " ", "AdditionalIncome[0].Label" },
        { "additional with unknown type", b => b["additionalIncome"]![0]!["type"] = "weekly", "AdditionalIncome[0].Type" },
        { "additional without a month", b => b["additionalIncome"]![1]!.AsObject().Remove("fromPeriodKey"), "AdditionalIncome[1].FromPeriodKey" },
        { "additional month before the FY", b => b["additionalIncome"]![1]!["fromPeriodKey"] = "2025-12", "AdditionalIncome[1].FromPeriodKey" },
        { "additional month after the FY", b => b["additionalIncome"]![0]!["fromPeriodKey"] = "2027-01", "AdditionalIncome[0].FromPeriodKey" },
        { "additional until before from", b => { b["additionalIncome"]![0]!["fromPeriodKey"] = "2026-05"; b["additionalIncome"]![0]!["toPeriodKey"] = "2026-04"; }, "AdditionalIncome[0].ToPeriodKey" },
        { "additional until after the FY", b => b["additionalIncome"]![0]!["toPeriodKey"] = "2027-01", "AdditionalIncome[0].ToPeriodKey" },
        { "no bank account", b => b["bankAccounts"] = new JsonArray(), "BankAccounts" },
        { "bank account names repeat ignoring case", b => b["bankAccounts"] = new JsonArray("SNB", " snb"), "BankAccounts" },
        { "bank account name over 40", b => b["bankAccounts"] = new JsonArray(new string('a', 41)), "BankAccounts[0]" },
        { "no category", b => b.Remove("categories"), "Categories" },
        { "category names repeat ignoring case", b => b["categories"] = new JsonArray("Food", "FOOD"), "Categories" },
        { "empty category name", b => b["categories"] = new JsonArray("Food", ""), "Categories[1]" },
    };

    // SET-01: Finish is blocked until everything required is filled in.
    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_setup_returns_400_and_saves_nothing(string reason, Action<JsonObject> change, string field)
    {
        _ = reason;
        var body = ValidRequest();
        change(body);
        var sub = NewSub();

        var response = await PostSetup(sub, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(problem!["errors"]!.AsObject().ContainsKey(field), problem.ToJsonString());
        Assert.Null(factory.Store.Saved(sub));
    }

    // Step 2 live preview (owner's choice, M3), from FiscalCalendar. The FY's months give step 3's
    // income month pickers (R-INC-03).
    [Fact]
    public async Task Preview_shows_the_current_fiscal_year_and_month()
    {
        var response = await Send(HttpMethod.Get, "/api/setup/preview?fiscalStartMonth=1&paydayDay=27&timezone=Asia/Riyadh", NewSub());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var fy = preview["fiscalYear"]!;
        Assert.Equal(("2026-01", "FY 2026", "2026-01-27", "2027-01-26", false),
            (fy["id"]!.GetValue<string>(), fy["label"]!.GetValue<string>(), fy["startDate"]!.GetValue<string>(),
                fy["endDate"]!.GetValue<string>(), fy["isTransition"]!.GetValue<bool>()));
        var periods = fy["periods"]!.AsArray();
        Assert.Equal(12, periods.Count);
        Assert.Equal(
            """{"key":"2026-01","label":"Jan 2026","startDate":"2026-01-27","endDate":"2026-02-26"}""",
            periods[0]!.ToJsonString());
        Assert.Equal(
            """{"key":"2026-12","label":"Dec 2026","startDate":"2026-12-27","endDate":"2027-01-26"}""",
            periods[^1]!.ToJsonString());
        Assert.Equal(
            """{"key":"2026-09","label":"Sep 2026","startDate":"2026-09-27","endDate":"2026-10-26"}""",
            preview["currentPeriod"]!.ToJsonString());
    }

    [Theory]
    [InlineData("fiscalStartMonth=13&paydayDay=27&timezone=Asia/Riyadh")]
    [InlineData("fiscalStartMonth=1&paydayDay=0&timezone=Asia/Riyadh")]
    [InlineData("fiscalStartMonth=1&paydayDay=27&timezone=Mars/Olympus")]
    [InlineData("fiscalStartMonth=1&paydayDay=27")]
    public async Task Preview_with_bad_settings_returns_400(string query)
    {
        var response = await Send(HttpMethod.Get, "/api/setup/preview?" + query, NewSub());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
