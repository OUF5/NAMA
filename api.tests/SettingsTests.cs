using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.7 Settings: GET /api/settings (owner's choice, M8, NFR-01) and PUT /api/profile (STG-01).
// Today is 17 Oct 2026 in Riyadh; FY 2026 runs Jan to Dec 2026 with payday 27.
public class SettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static JsonObject Profile(string displayName = "Mohammed", string projectName = "Family budget",
        string currency = "QAR", string timezone = "Asia/Qatar") => new()
    {
        ["displayName"] = displayName,
        ["projectName"] = projectName,
        ["currency"] = currency,
        ["timezone"] = timezone,
    };

    [Fact]
    public async Task Get_returns_every_section()
    {
        var sub = await SetUpUser(factory, [new("s", IncomeKind.Salary, "Salary", 1_500_000, "2026-01", null)]);

        var response = await Send(factory, HttpMethod.Get, "/api/settings", sub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await Json(response);
        Assert.Equal(("Home budget", "SAR", "Asia/Riyadh"),
            (settings["profile"]!["projectName"]!.GetValue<string>(), settings["profile"]!["currency"]!.GetValue<string>(),
                settings["profile"]!["timezone"]!.GetValue<string>()));
        // STG-02: the settings apply from the FY after FY 2026.
        var calendar = settings["calendar"]!;
        Assert.Equal((1, 27, "2027-01-27", "FY 2027"),
            (calendar["fiscalStartMonth"]!.GetValue<int>(), calendar["paydayDay"]!.GetValue<int>(),
                calendar["takesEffectFrom"]!.GetValue<string>(), calendar["nextFiscalYear"]!["label"]!.GetValue<string>()));
        Assert.Equal(15_000m, settings["income"]!["currentTotal"]!.GetValue<decimal>());
        // Bank accounts live on Banks (BNK-02).
        Assert.False(settings.AsObject().ContainsKey("bankAccounts"));
        // Categories live on Plan's Categories tab (owner note 9).
        Assert.False(settings.AsObject().ContainsKey("categories"));
    }

    // STG-01: the answer is the new /api/me; the time zone moves "today" (R-CAL-10).
    [Fact]
    public async Task Profile_change_answers_with_the_new_me()
    {
        var sub = await SetUpUser(factory);
        // 17 Oct 2026, 22:30 UTC: still 17 Oct in Doha (UTC+3), already 18 Oct in Tokyo (UTC+9).
        factory.Time.UtcNow = new DateTimeOffset(2026, 10, 17, 22, 30, 0, TimeSpan.Zero);
        try
        {
            var response = await Send(factory, HttpMethod.Put, "/api/profile", sub, Profile(displayName: "  Mohammed  ", timezone: "Asia/Tokyo"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var me = await Json(response);
            Assert.Equal(("Mohammed", "Family budget", "QAR", "Asia/Tokyo", "2026-10-18"),
                (me["profile"]!["displayName"]!.GetValue<string>(), me["profile"]!["projectName"]!.GetValue<string>(),
                    me["profile"]!["currency"]!.GetValue<string>(), me["profile"]!["timezone"]!.GetValue<string>(),
                    me["today"]!.GetValue<string>()));
            // The rest of the profile is unchanged.
            var stored = (await factory.Store.GetProfileAsync(sub))!;
            Assert.Equal((ApiFactory.AllowedEmail, 1, 27, true), (stored.Email, stored.FiscalStartMonth, stored.PaydayDay, stored.SetupCompleted));
        }
        finally
        {
            factory.Time.UtcNow = TestTime.Default;
        }
    }

    public static TheoryData<JsonObject, string> InvalidProfiles => new()
    {
        { Profile(displayName: " "), "DisplayName" },
        { Profile(projectName: new string('x', 41)), "ProjectName" },
        { Profile(currency: "JPY"), "Currency" },
        { Profile(timezone: "Mars/Olympus"), "Timezone" },
        { Profile(timezone: ""), "Timezone" },
    };

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public async Task Invalid_profile_values_return_400(JsonObject body, string field)
    {
        var sub = await SetUpUser(factory);

        var response = await Send(factory, HttpMethod.Put, "/api/profile", sub, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.True(problem["errors"]!.AsObject().ContainsKey(field), problem.ToJsonString());
        Assert.Equal("Home budget", (await factory.Store.GetProfileAsync(sub))!.ProjectName);
    }

    [Theory]
    [InlineData("GET", "/api/settings")]
    [InlineData("PUT", "/api/profile")]
    public async Task A_user_who_is_not_set_up_gets_404(string method, string url)
    {
        var body = method == "PUT" ? Profile() : null;

        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, new HttpMethod(method), url, NewSub(), body)).StatusCode);
    }
}
