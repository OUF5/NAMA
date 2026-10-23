using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Tests;

// PRD 7.5 GET /api/me after setup: profile, current FY and month (UX-02), with "today" in the
// profile time zone (R-CAL-10).
public class MeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<JsonObject> GetMe(string sub, DateTimeOffset utcNow)
    {
        factory.Time.UtcNow = utcNow;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(sub: sub));
        var response = await factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task<string> SetUp()
    {
        var sub = "user-" + Guid.NewGuid().ToString("N");
        var profile = new UserProfile("Owner", ApiFactory.AllowedEmail, "Home budget", "SAR", "Asia/Riyadh", 1, 27, true, null);
        var fiscalYear = FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 10, 17), 1, 27);
        await factory.Store.CreateSetupAsync(sub, new SetupData(profile, fiscalYear, [], [], [], new IncomeSplit(fiscalYear.Id, new Dictionary<string, int>())));
        return sub;
    }

    [Fact]
    public async Task Me_after_setup_returns_the_profile_and_the_current_month()
    {
        var me = await GetMe(await SetUp(), TestTime.Default);

        Assert.Equal(
            """{"setupCompleted":true,"isAdmin":true,"profile":{"displayName":"Owner","email":"owner@example.com","projectName":"Home budget","currency":"SAR","timezone":"Asia/Riyadh","fiscalStartMonth":1,"paydayDay":27,"monthNaming":"end","lastBankAccountId":null},"currentFyId":"2026-01","currentPeriodKey":"2026-09","currentPeriod":{"key":"2026-09","label":"Oct 2026","startDate":"2026-09-27","endDate":"2026-10-26"},"today":"2026-10-17","firstDate":"2026-01-27","lastDate":"2027-01-26","monthKeyOffset":1}""",
            me.ToJsonString());
    }

    // 26 Oct 22:00 UTC is already 27 Oct 01:00 in Riyadh, so the Oct 2026 month has started.
    [Theory]
    [InlineData("2026-10-26T20:59:59Z", "2026-09")]
    [InlineData("2026-10-26T22:00:00Z", "2026-10")]
    public async Task Today_is_taken_in_the_profile_time_zone(string utcNow, string expectedPeriodKey)
    {
        var me = await GetMe(await SetUp(), DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expectedPeriodKey, me["currentPeriodKey"]!.GetValue<string>());
    }

    // R-CAL-10: the Add form's default date is today in the profile time zone.
    [Theory]
    [InlineData("2026-10-17T20:59:59Z", "2026-10-17")]
    [InlineData("2026-10-17T21:00:00Z", "2026-10-18")]
    public async Task Today_is_the_date_in_the_profile_time_zone(string utcNow, string expectedToday)
    {
        var me = await GetMe(await SetUp(), DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expectedToday, me["today"]!.GetValue<string>());
    }

    // Decision 5: null after setup, then the account of the last entry.
    [Fact]
    public async Task Last_bank_account_follows_the_last_entry()
    {
        var sub = await SetUp();
        Assert.Null((await GetMe(sub, TestTime.Default))["profile"]!["lastBankAccountId"]);

        await factory.Store.AddEntryAsync(sub, new Entry("e", new DateOnly(2026, 10, 17), "2026-09", "2026-01", "food", "groceries", "rajhi", 100, ""));

        Assert.Equal("rajhi", (await GetMe(sub, TestTime.Default))["profile"]!["lastBankAccountId"]!.GetValue<string>());
    }

    // After the last stored FY ends (rollover is M8) there is no current FY or month.
    [Fact]
    public async Task Me_after_the_last_fiscal_year_has_no_current_month()
    {
        var me = await GetMe(await SetUp(), new DateTimeOffset(2027, 2, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.True(me["setupCompleted"]!.GetValue<bool>());
        Assert.Null(me["currentFyId"]);
        Assert.Null(me["currentPeriod"]);
        // FY-03: the Add form still knows the range of the stored FYs.
        Assert.Equal(("2027-02-01", "2026-01-27", "2027-01-26"),
            (me["today"]!.GetValue<string>(), me["firstDate"]!.GetValue<string>(), me["lastDate"]!.GetValue<string>()));
    }
}
