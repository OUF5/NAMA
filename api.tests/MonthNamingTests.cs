using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// R-CAL-11 (owner note 19): a month is named by the month it ends in, or by the month the salary
// arrives. Only the names change: the dates, keys, FYs and amounts stay. Today is 17 Oct 2026 in
// Riyadh; FY 2026 runs 27 Jan 2026 to 26 Jan 2027 with payday 27.
public class MonthNamingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private Task<HttpResponseMessage> PutCalendar(string sub, string? monthNaming)
    {
        var body = new JsonObject { ["fiscalStartMonth"] = 1, ["paydayDay"] = 27 };
        if (monthNaming is not null)
        {
            body["monthNaming"] = monthNaming;
        }
        return Send(factory, HttpMethod.Put, "/api/settings/calendar", sub, body);
    }

    private async Task<string> SetUpWithMoney()
    {
        var sub = await SetUpUser(factory, [
            new("salary", IncomeKind.Salary, "Salary", 1_500_000, "2026-01", null),
            new("gift", IncomeKind.Additional, "Gift", 50_000, "2026-09", "2026-09", IncomeCounts.Actual, "rajhi"),
        ]);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.SetPlanAsync(sub, "2026-01", new PlanAmounts("groceries", "food",
            new Dictionary<string, long> { ["2026-09"] = 150_000, ["2026-10"] = 150_000 }));
        await factory.Store.AddEntryAsync(sub, new Entry("e1", new DateOnly(2026, 10, 17), "2026-09", "2026-01", "food", "groceries", "bank", 12_550, ""));
        return sub;
    }

    // Every "label" removed, so what is left is the dates, keys and amounts.
    private static JsonNode? WithoutLabels(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                {
                    if (key == "label")
                    {
                        obj.Remove(key);
                    }
                    else
                    {
                        WithoutLabels(obj[key]);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    WithoutLabels(item);
                }
                break;
        }
        return node;
    }

    private async Task<string> Numbers(string sub)
    {
        var parts = new List<string>();
        foreach (var url in new[] { "/api/fiscal-years", "/api/fiscal-years/2026-01/overview", "/api/fiscal-years/2026-01/months/2026-09",
                     "/api/fiscal-years/2026-01/plans", "/api/summary" })
        {
            var response = await Send(factory, HttpMethod.Get, url, sub);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            parts.Add(WithoutLabels(await Json(response))!.ToJsonString());
        }
        return string.Join("\n", parts);
    }

    private static IEnumerable<string> Labels(JsonNode fiscalYears) =>
        fiscalYears[0]!["periods"]!.AsArray().Select(period => period!["label"]!.GetValue<string>());

    // Months stored before 23 Oct 2026 keep their dates and stored labels; they are named when read.
    [Fact]
    public async Task Stored_months_are_named_by_the_month_they_end_in()
    {
        var sub = await SetUpUser(factory);

        var fiscalYears = await Json(await Send(factory, HttpMethod.Get, "/api/fiscal-years", sub));

        Assert.Equal(
            """{"key":"2026-01","label":"Feb 2026","startDate":"2026-01-27","endDate":"2026-02-26"}""",
            fiscalYears[0]!["periods"]![0]!.ToJsonString());
        Assert.Equal("FY 2026", fiscalYears[0]!["label"]!.GetValue<string>());
        Assert.Equal("Jan 2026", (await factory.Store.GetStoredFiscalYearsAsync(sub))[0].Periods[0].Label);
    }

    // STG-02 with R-CAL-11: renamed right away, every amount the same, and back again.
    [Fact]
    public async Task Changing_the_month_naming_renames_every_month_and_changes_no_number()
    {
        var sub = await SetUpWithMoney();
        var before = await Numbers(sub);
        Assert.Equal(
            ["Feb 2026", "Mar 2026", "Apr 2026", "May 2026", "Jun 2026", "Jul 2026", "Aug 2026", "Sep 2026", "Oct 2026", "Nov 2026", "Dec 2026", "Jan 2027"],
            Labels(await Json(await Send(factory, HttpMethod.Get, "/api/fiscal-years", sub))));

        var response = await PutCalendar(sub, "payday");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var calendar = await Json(response);
        Assert.Equal(("payday", 0, "Jan 2027"),
            (calendar["monthNaming"]!.GetValue<string>(), calendar["monthKeyOffset"]!.GetValue<int>(),
                calendar["nextFiscalYear"]!["periods"]![0]!["label"]!.GetValue<string>()));
        Assert.Equal(
            ["Jan 2026", "Feb 2026", "Mar 2026", "Apr 2026", "May 2026", "Jun 2026", "Jul 2026", "Aug 2026", "Sep 2026", "Oct 2026", "Nov 2026", "Dec 2026"],
            Labels(await Json(await Send(factory, HttpMethod.Get, "/api/fiscal-years", sub))));
        var me = await Json(await Send(factory, HttpMethod.Get, "/api/me", sub));
        Assert.Equal(("payday", "2026-09", "Sep 2026", 0),
            (me["profile"]!["monthNaming"]!.GetValue<string>(), me["currentPeriod"]!["key"]!.GetValue<string>(),
                me["currentPeriod"]!["label"]!.GetValue<string>(), me["monthKeyOffset"]!.GetValue<int>()));
        Assert.Equal(before, await Numbers(sub));

        Assert.Equal(HttpStatusCode.OK, (await PutCalendar(sub, "end")).StatusCode);
        Assert.Equal("Oct 2026", (await Json(await Send(factory, HttpMethod.Get, "/api/me", sub)))["currentPeriod"]!["label"]!.GetValue<string>());
        Assert.Equal(before, await Numbers(sub));
    }

    // The start month and payday can be saved without the naming; it stays as it is.
    [Fact]
    public async Task Calendar_without_month_naming_keeps_it()
    {
        var sub = await SetUpUser(factory);
        await PutCalendar(sub, "payday");

        var calendar = await Json(await PutCalendar(sub, null));

        Assert.Equal("payday", calendar["monthNaming"]!.GetValue<string>());
        Assert.Equal(MonthNaming.PaydayMonth, (await factory.Store.GetProfileAsync(sub))!.MonthNaming);
    }

    [Fact]
    public async Task Unknown_month_naming_returns_400_and_saves_nothing()
    {
        var sub = await SetUpUser(factory);

        var response = await PutCalendar(sub, "start");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response))["errors"]!.AsObject().ContainsKey("MonthNaming"));
        Assert.Equal(MonthNaming.EndMonth, (await factory.Store.GetProfileAsync(sub))!.MonthNaming);
    }
}
