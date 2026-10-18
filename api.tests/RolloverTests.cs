using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// M8 "Done when": the R-CAL-08 rollover examples end to end (FY-01, FY-02, FY-03, R-CAL-07,
// R-STA-03, STG-02). FY 2026 runs Jan to Dec 2026 with payday 27 (27 Jan 2026 to 26 Jan 2027).
// Today is set per step at 12:00 in Riyadh (UTC+3, R-CAL-10).
public class RolloverTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Fy2026 = "2026-01";

    private static readonly string[] Months2026 =
        ["2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-11", "2026-12"];

    // The M4 plans: Groceries 1,500 every month, Fuel 400 from Sep. Old Gym is archived and has a plan.
    private async Task<string> SetUp()
    {
        var sub = await SetUpUser(factory);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("fuel", "transport", "Fuel", 0));
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("gym", "food", "Old Gym", 1, Archived: true));
        await factory.Store.SetPlanAsync(sub, Fy2026, new PlanAmounts("groceries", "food", Months2026.ToDictionary(key => key, _ => 150_000L)));
        await factory.Store.SetPlanAsync(sub, Fy2026, new PlanAmounts("fuel", "transport",
            new Dictionary<string, long> { ["2026-09"] = 40_000, ["2026-10"] = 40_000, ["2026-11"] = 40_000, ["2026-12"] = 40_000 }));
        await factory.Store.SetPlanAsync(sub, Fy2026, new PlanAmounts("gym", "food", Months2026.ToDictionary(key => key, _ => 10_000L)));
        return sub;
    }

    private void Today(int year, int month, int day) =>
        factory.Time.UtcNow = new DateTimeOffset(year, month, day, 9, 0, 0, TimeSpan.Zero);

    private Task<HttpResponseMessage> PutCalendar(string sub, int fiscalStartMonth, int paydayDay) =>
        Send(factory, HttpMethod.Put, "/api/settings/calendar", sub,
            new JsonObject { ["fiscalStartMonth"] = fiscalStartMonth, ["paydayDay"] = paydayDay });

    private Task<HttpResponseMessage> StartNext(string sub, bool copyPlans = true) =>
        Send(factory, HttpMethod.Post, "/api/fiscal-years/next", sub, new JsonObject { ["copyPlans"] = copyPlans });

    private async Task<JsonNode> StartNextOk(string sub, bool copyPlans = true)
    {
        var response = await StartNext(sub, copyPlans);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private Task<HttpResponseMessage> AddEntry(string sub, string date) =>
        Send(factory, HttpMethod.Post, "/api/entries", sub, new JsonObject
        {
            ["subCategoryId"] = "groceries",
            ["bankAccountId"] = "bank",
            ["date"] = date,
            ["amount"] = 10m,
        });

    private async Task AssertEntryIn(string sub, string date, string periodKey, string fyId)
    {
        var response = await AddEntry(sub, date);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await Json(response);
        Assert.Equal((periodKey, fyId), (entry["periodKey"]!.GetValue<string>(), entry["fyId"]!.GetValue<string>()));
    }

    private async Task AssertEntryRefused(string sub, string date) =>
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await AddEntry(sub, date)).StatusCode);

    private async Task<FiscalYear> Stored(string sub, string fyId) =>
        (await factory.Store.GetFiscalYearsAsync(sub)).Single(fiscalYear => fiscalYear.Id == fyId);

    private async Task<Dictionary<string, IReadOnlyDictionary<string, long>>> Plans(string sub, string fyId) =>
        (await factory.Store.GetPlansAsync(sub, fyId)).ToDictionary(plan => plan.SubCategoryId, plan => plan.Amounts);

    private static string Str(JsonNode? node) => node!.GetValue<string>();

    private async Task<JsonNode?> Rollover(string sub)
    {
        var response = await Send(factory, HttpMethod.Get, $"/api/fiscal-years/{Fy2026}/overview", sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response))["rollover"];
    }

    // R-CAL-08 Example A: the start month changes to July. Transition FY Jan to Jun 2027, then
    // FY 2027/28 Jul 2027 to Jun 2028. Plans copy the same month a year earlier (owner's choice, M8).
    [Fact]
    public async Task Example_A_start_month_change_goes_through_a_transition_fiscal_year()
    {
        var sub = await SetUp();
        Today(2026, 10, 17);

        // STG-02: saved now, applied from the FY after the latest one (R-CAL-07).
        var calendarResponse = await PutCalendar(sub, 7, 27);
        Assert.Equal(HttpStatusCode.OK, calendarResponse.StatusCode);
        var calendar = await Json(calendarResponse);
        Assert.Equal("2027-01-27", Str(calendar["takesEffectFrom"]));
        var preview = calendar["nextFiscalYear"]!;
        Assert.Equal(("FY 2027", "2027-01-27", "2027-07-26", true, 6),
            (Str(preview["label"]), Str(preview["startDate"]), Str(preview["endDate"]),
                preview["isTransition"]!.GetValue<bool>(), preview["periods"]!.AsArray().Count));
        Assert.Equal((7, 27), ((await factory.Store.GetProfileAsync(sub))!.FiscalStartMonth, (await factory.Store.GetProfileAsync(sub))!.PaydayDay));
        // The current FY keeps its dates.
        Assert.Equal(new DateOnly(2027, 1, 26), (await Stored(sub, Fy2026)).EndDate);

        // FY-01: 409 before the last month (Dec 2026 = 27 Dec 2026 to 26 Jan 2027).
        Assert.Equal(HttpStatusCode.Conflict, (await StartNext(sub)).StatusCode);
        Today(2026, 12, 26);
        Assert.Equal(HttpStatusCode.Conflict, (await StartNext(sub)).StatusCode);
        Assert.Null(await Rollover(sub));

        Today(2026, 12, 27);
        var rollover = (await Rollover(sub))!;
        Assert.Equal(("2026-01", "FY 2026", "2027-01-26", "FY 2027", "2027-01-27"),
            (Str(rollover["fyId"]), Str(rollover["label"]), Str(rollover["endDate"]), Str(rollover["nextLabel"]), Str(rollover["nextStartDate"])));

        var started = await StartNextOk(sub);
        Assert.Equal("2027-01", Str(started["fiscalYear"]!["id"]));
        Assert.Equal(1, started["copiedPlans"]!.GetValue<int>());
        Assert.Equal("2027-07-26", Str(started["me"]!["lastDate"]));
        // Today is still in FY 2026.
        Assert.Equal(Fy2026, Str(started["me"]!["currentFyId"]));

        var transition = await Stored(sub, "2027-01");
        Assert.True(transition.IsTransition);
        Assert.Equal("FY 2027", transition.Label);
        Assert.Equal((new DateOnly(2027, 1, 27), new DateOnly(2027, 7, 26)), (transition.StartDate, transition.EndDate));
        Assert.Equal(["Jan 2027", "Feb 2027", "Mar 2027", "Apr 2027", "May 2027", "Jun 2027"], transition.Periods.Select(p => p.Label));

        // Groceries Jan to Jun 2026 → Jan to Jun 2027. Fuel had nothing then; Old Gym is archived.
        var transitionPlans = await Plans(sub, "2027-01");
        Assert.Equal(["groceries"], transitionPlans.Keys);
        Assert.Equal(transition.Periods.Select(p => p.Key), transitionPlans["groceries"].Keys.Order());
        Assert.All(transitionPlans["groceries"].Values, amount => Assert.Equal(150_000, amount));

        // A second Start is 409: the latest FY is now the transition FY, whose last month starts 27 Jun 2027.
        Assert.Equal(HttpStatusCode.Conflict, (await StartNext(sub)).StatusCode);
        Assert.Null(await Rollover(sub));

        // R-STA-03, FY-03: entries can be dated in the new FY, and not after it.
        await AssertEntryIn(sub, "2027-03-15", "2027-02", "2027-01");
        await AssertEntryRefused(sub, "2027-07-27");

        // FY 2027/28 follows the transition FY.
        Today(2027, 6, 27);
        var fy2027Response = await StartNextOk(sub);
        Assert.Equal("2027-07", Str(fy2027Response["fiscalYear"]!["id"]));
        var fy2027 = await Stored(sub, "2027-07");
        Assert.False(fy2027.IsTransition);
        Assert.Equal("FY 2027/28", fy2027.Label);
        Assert.Equal(12, fy2027.Periods.Count);
        Assert.Equal((new DateOnly(2027, 7, 27), new DateOnly(2028, 7, 26)), (fy2027.StartDate, fy2027.EndDate));

        // Jul to Dec 2027 from FY 2026, Jan to Jun 2028 from the transition FY.
        var fy2027Plans = await Plans(sub, "2027-07");
        Assert.Equal(["fuel", "groceries"], fy2027Plans.Keys.Order());
        Assert.Equal(fy2027.Periods.Select(p => p.Key), fy2027Plans["groceries"].Keys.Order());
        Assert.All(fy2027Plans["groceries"].Values, amount => Assert.Equal(150_000, amount));
        Assert.Equal(
            new Dictionary<string, long> { ["2027-09"] = 40_000, ["2027-10"] = 40_000, ["2027-11"] = 40_000, ["2027-12"] = 40_000 },
            fy2027Plans["fuel"]);

        await AssertEntryIn(sub, "2028-07-26", "2028-06", "2027-07");
        await AssertEntryRefused(sub, "2028-07-27");
    }

    // R-CAL-08 Example B: payday changes from 27 to 1. Jan 2027 = 27 to 31 Jan, Feb 2027 = 1 to 28 Feb.
    [Fact]
    public async Task Example_B_payday_change_gives_a_short_first_month()
    {
        var sub = await SetUp();
        Today(2026, 10, 17);

        var calendar = await Json(await PutCalendar(sub, 1, 1));
        Assert.Equal("2027-01-27", Str(calendar["takesEffectFrom"]));
        Assert.Equal(("FY 2027", "2027-12-31"), (Str(calendar["nextFiscalYear"]!["label"]), Str(calendar["nextFiscalYear"]!["endDate"])));

        Today(2027, 1, 26);
        var started = await StartNextOk(sub);
        Assert.Equal("2027-01", Str(started["fiscalYear"]!["id"]));

        var fy2027 = await Stored(sub, "2027-01");
        Assert.False(fy2027.IsTransition);
        Assert.Equal(1, fy2027.PaydayDay);
        Assert.Equal(new Period("2027-01", "Jan 2027", new(2027, 1, 27), new(2027, 1, 31)), fy2027.Periods[0]);
        Assert.Equal(new Period("2027-02", "Feb 2027", new(2027, 2, 1), new(2027, 2, 28)), fy2027.Periods[1]);
        Assert.Equal(new DateOnly(2027, 12, 31), fy2027.EndDate);

        // The same month a year earlier: Groceries every month, Fuel Sep to Dec.
        var plans = await Plans(sub, "2027-01");
        Assert.Equal(12, plans["groceries"].Count);
        Assert.Equal(["2027-09", "2027-10", "2027-11", "2027-12"], plans["fuel"].Keys.Order());

        await AssertEntryIn(sub, "2027-01-31", "2027-01", "2027-01");
        await AssertEntryIn(sub, "2027-02-01", "2027-02", "2027-01");
        await AssertEntryIn(sub, "2027-01-26", "2026-12", Fy2026);
        await AssertEntryRefused(sub, "2028-01-01");
    }

    // FY-02: several FYs behind means one FY per call, until today is inside the latest one.
    [Fact]
    public async Task One_fiscal_year_per_call_when_several_are_behind()
    {
        var sub = await SetUp();
        Today(2028, 3, 1);

        Assert.Equal("2027-01", Str((await StartNextOk(sub))["fiscalYear"]!["id"]));
        Assert.NotNull(await Rollover(sub));
        var second = await StartNextOk(sub);
        Assert.Equal("2028-01", Str(second["fiscalYear"]!["id"]));
        Assert.Equal("2028-01", Str(second["me"]!["currentFyId"]));
        Assert.Equal(HttpStatusCode.Conflict, (await StartNext(sub)).StatusCode);
        Assert.Null(await Rollover(sub));
        Assert.Equal(3, (await factory.Store.GetFiscalYearsAsync(sub)).Count);
    }

    [Fact]
    public async Task Without_copy_no_plans_are_created()
    {
        var sub = await SetUp();
        Today(2026, 12, 27);

        var started = await StartNextOk(sub, copyPlans: false);

        Assert.Equal(0, started["copiedPlans"]!.GetValue<int>());
        Assert.Empty(await factory.Store.GetPlansAsync(sub, "2027-01"));
    }

    [Fact]
    public async Task A_body_without_copyPlans_returns_400()
    {
        var sub = await SetUp();
        Today(2026, 12, 27);

        var response = await Send(factory, HttpMethod.Post, "/api/fiscal-years/next", sub, new JsonObject());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(await factory.Store.GetFiscalYearsAsync(sub));
    }

    // R-CAL-07: when the next FY was already started, a change waits for the FY after it.
    [Fact]
    public async Task A_change_after_the_next_fiscal_year_started_waits_for_the_one_after_it()
    {
        var sub = await SetUp();
        Today(2027, 1, 1);
        await StartNextOk(sub);

        var calendar = await Json(await PutCalendar(sub, 1, 1));

        Assert.Equal("2028-01-27", Str(calendar["takesEffectFrom"]));
        Assert.Equal(27, (await Stored(sub, "2027-01")).PaydayDay);
        Assert.Equal(new DateOnly(2028, 1, 26), (await Stored(sub, "2027-01")).EndDate);
    }

    [Theory]
    [InlineData(0, 27)]
    [InlineData(13, 27)]
    [InlineData(1, 0)]
    [InlineData(1, 32)]
    public async Task Calendar_values_out_of_range_return_400(int fiscalStartMonth, int paydayDay)
    {
        var sub = await SetUp();

        Assert.Equal(HttpStatusCode.BadRequest, (await PutCalendar(sub, fiscalStartMonth, paydayDay)).StatusCode);
        Assert.Equal((1, 27), ((await factory.Store.GetProfileAsync(sub))!.FiscalStartMonth, (await factory.Store.GetProfileAsync(sub))!.PaydayDay));
    }
}
