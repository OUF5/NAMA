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
        await factory.Store.CreateSetupAsync(sub, new SetupData(profile, fiscalYear, [], [], []));
        return sub;
    }

    [Fact]
    public async Task Me_after_setup_returns_the_profile_and_the_current_month()
    {
        var me = await GetMe(await SetUp(), TestTime.Default);

        Assert.Equal(
            """{"setupCompleted":true,"profile":{"displayName":"Owner","email":"owner@example.com","projectName":"Home budget","currency":"SAR","timezone":"Asia/Riyadh","fiscalStartMonth":1,"paydayDay":27},"currentFyId":"2026-01","currentPeriodKey":"2026-09","currentPeriod":{"key":"2026-09","label":"Sep 2026","startDate":"2026-09-27","endDate":"2026-10-26"}}""",
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

    // After the last stored FY ends (rollover is M8) there is no current FY or month.
    [Fact]
    public async Task Me_after_the_last_fiscal_year_has_no_current_month()
    {
        var me = await GetMe(await SetUp(), new DateTimeOffset(2027, 2, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.True(me["setupCompleted"]!.GetValue<bool>());
        Assert.Null(me["currentFyId"]);
        Assert.Null(me["currentPeriod"]);
    }
}
