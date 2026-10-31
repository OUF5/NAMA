using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 5.14 Projects (owner note 30): GET /api/projects, POST, PUT, status and DELETE. Today is 17 Oct 2026 in Riyadh; FY 2026 runs 27 Jan
// 2026 to 26 Jan 2027 with payday 27, so the month in progress is 27 Sep to 26 Oct (key 2026-09).
public class ProjectsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly FyStart = new(2026, 1, 27);

    // SNB 5,000 (gets the salary of 15,000), Al Rajhi 20,000 and Alinma 8,000 at the FY's start, and
    // Groceries to spend on.
    private async Task<string> SetUpWithMoney()
    {
        var sub = await SetUpUser(factory, [new("salary", IncomeKind.Salary, "Salary", 1_500_000, "2026-01", null)], accounts:
        [
            new BankAccount("bank", "SNB", Opening: new OpeningBalance(500_000, FyStart)),
            new BankAccount("rajhi", "Al Rajhi", Opening: new OpeningBalance(2_000_000, FyStart)),
            new BankAccount("alinma", "Alinma", Opening: new OpeningBalance(800_000, FyStart)),
        ]);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        return sub;
    }

    private static readonly string[] PlanKeys = ["2026-09", "2026-10", "2026-11", "2026-12", "2027-01", "2027-02"];

    private static JsonObject Request(
        string name = "Road to DE",
        string deadline = "2027-03-15",
        decimal? best = 25_000,
        decimal? likely = 30_000,
        decimal? worst = null,
        string active = "likely",
        string account = "rajhi",
        decimal monthly = 2_000,
        Dictionary<string, decimal>? amounts = null,
        JsonArray? milestones = null)
    {
        // R-PRJ-05: by default the same amount in each month from 27 Sep 2026 to the one holding 15 Mar 2027.
        amounts ??= PlanKeys.ToDictionary(key => key, _ => monthly);
        var body = new JsonObject
        {
            ["name"] = name,
            ["deadline"] = deadline,
            ["best"] = best,
            ["likely"] = likely,
            ["worst"] = worst,
            ["activeTarget"] = active,
            ["bankAccountId"] = account,
            ["monthly"] = new JsonArray(amounts.Select(amount => (JsonNode)new JsonObject { ["periodKey"] = amount.Key, ["amount"] = amount.Value }).ToArray()),
            ["milestones"] = milestones ?? new JsonArray(new JsonObject { ["label"] = "Deposit", ["date"] = "2026-12-01", ["amount"] = 22_000 }),
        };
        return body;
    }

    private async Task<JsonNode> Post(string sub, JsonObject body)
    {
        var response = await Send(factory, HttpMethod.Post, "/api/projects", sub, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private async Task<JsonNode> Get(string sub)
    {
        var response = await Send(factory, HttpMethod.Get, "/api/projects", sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static JsonNode Project(JsonNode response, string name) =>
        response["projects"]!.AsArray().First(project => project!["name"]!.GetValue<string>() == name)!;

    private static string Id(JsonNode response, string name) => Project(response, name)["id"]!.GetValue<string>();

    private static decimal? Amount(JsonNode? node) => node?.GetValue<decimal>();

    private async Task<HttpResponseMessage> Expense(string sub, string bankAccountId, decimal amount, string date = "2026-10-17") =>
        await Send(factory, HttpMethod.Post, "/api/entries", sub, new JsonObject
        {
            ["subCategoryId"] = "groceries",
            ["bankAccountId"] = bankAccountId,
            ["date"] = date,
            ["amount"] = amount,
        });

    // R-PRJ-03 to R-PRJ-09 on real balances: Al Rajhi holds 20,000 and 1,200 went in this month.
    [Fact]
    public async Task A_project_reads_its_accounts_and_projects_to_the_deadline()
    {
        var sub = await SetUpWithMoney();
        await factory.Store.AddTransferAsync(sub, new Transfer("t1", new DateOnly(2026, 10, 10), "2026-09", "2026-01", "bank", "rajhi", 120_000, ""));

        var project = Project(await Post(sub, Request()), "Road to DE");

        Assert.Equal("active", project["status"]!.GetValue<string>());
        Assert.Equal("2026-09", project["startPeriodKey"]!.GetValue<string>());
        Assert.Equal(21_200m, Amount(project["money"]));
        Assert.Equal("rajhi", project["bankAccountId"]!.GetValue<string>());
        Assert.Equal(21_200m, Amount(project["progress"]));
        Assert.Equal(30_000m, Amount(project["target"]));
        Assert.Equal(8_800m, Amount(project["stillToSave"]));
        Assert.Equal(70, project["percent"]!.GetValue<int>());

        // R-PRJ-06: 1,200 of 2,000 is in, so 800 is still to put in.
        var month = Assert.Single(project["months"]!.AsArray());
        Assert.Equal((2_000m, 1_200m, 800m), (Amount(month!["planned"]), Amount(month["actual"]), Amount(month["behind"])));
        Assert.Equal(800m, Amount(project["stillToPutIn"]));

        // R-PRJ-07: 27 Oct to 27 Feb start by 15 Mar 2027 (the last two as FY 2027 would have them):
        // 21,200 + 800 + 5 × 2,000 = 32,000.
        Assert.Equal(["2026-10", "2026-11", "2026-12", "2027-01", "2027-02"],
            project["monthsToCome"]!.AsArray().Select(item => item!["period"]!["key"]!.GetValue<string>()));
        Assert.Equal(32_000m, Amount(project["projected"]));
        Assert.Equal(0m, Amount(project["short"]));
        Assert.Equal(6, project["monthsLeft"]!.GetValue<int>());
        Assert.Equal("onTrack", project["state"]!.GetValue<string>());
        Assert.Equal([true, true], project["targets"]!.AsArray().Select(target => target!["covered"]!.GetValue<bool>()));
        Assert.Equal(PlanKeys, project["plan"]!.AsArray().Select(month => month!["period"]!["key"]!.GetValue<string>()));

        // R-PRJ-08: 21,200 + 800 + 2 × 2,000 (27 Oct, 27 Nov) = 26,000 by 1 Dec.
        var milestone = Assert.Single(project["milestones"]!.AsArray());
        Assert.Equal((false, 26_000m, 4_000m), (milestone!["passed"]!.GetValue<bool>(), Amount(milestone["money"]), Amount(milestone["difference"])));
    }

    // R-PRJ-04: the project is its account's balance, so an expense from that account lowers progress
    // and one from another account does not touch it.
    [Fact]
    public async Task An_expense_from_its_account_lowers_progress()
    {
        var sub = await SetUpWithMoney();
        await Post(sub, Request());

        Assert.Equal(HttpStatusCode.Created, (await Expense(sub, "rajhi", 500)).StatusCode);
        var own = Project(await Get(sub), "Road to DE");
        Assert.Equal((19_500m, 19_500m, -500m), (Amount(own["money"]), Amount(own["progress"]), Amount(own["months"]![0]!["actual"])));

        Assert.Equal(HttpStatusCode.Created, (await Expense(sub, "bank", 300)).StatusCode);
        Assert.Equal(19_500m, Amount(Project(await Get(sub), "Road to DE")["progress"]));
    }

    // R-PRJ-03: an account counts in at most one project that is not Done.
    [Fact]
    public async Task An_account_counts_in_one_project_until_it_is_done()
    {
        var sub = await SetUpWithMoney();
        var id = Id(await Post(sub, Request()), "Road to DE");

        var taken = await Send(factory, HttpMethod.Post, "/api/projects", sub, Request(name: "Car", account: "rajhi"));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Contains("Al Rajhi is already counted by Road to DE.", await taken.Content.ReadAsStringAsync());

        var done = await Send(factory, HttpMethod.Put, $"/api/projects/{id}/status", sub, new JsonObject { ["status"] = "done", ["doneOn"] = "2026-10-16" });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var car = await Post(sub, Request(name: "Car", account: "rajhi"));
        var rajhi = car["accounts"]!.AsArray().Single(account => account!["id"]!.GetValue<string>() == "rajhi")!;
        Assert.Equal(Id(car, "Car"), rajhi["projectId"]!.GetValue<string>());

        var reopen = await Send(factory, HttpMethod.Put, $"/api/projects/{id}/status", sub, new JsonObject { ["status"] = "active" });
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);
    }

    // R-PRJ-01: Mark done keeps the figures of its day.
    [Fact]
    public async Task A_done_project_stays_as_it_was_on_its_day()
    {
        var sub = await SetUpWithMoney();
        var id = Id(await Post(sub, Request()), "Road to DE");
        await factory.Store.AddTransferAsync(sub, new Transfer("t1", new DateOnly(2026, 10, 17), "2026-09", "2026-01", "bank", "rajhi", 50_000, ""));

        var response = await Json(await Send(factory, HttpMethod.Put, $"/api/projects/{id}/status", sub,
            new JsonObject { ["status"] = "done", ["doneOn"] = "2026-10-16" }));

        var project = Project(response, "Road to DE");
        Assert.Equal(("done", "done", "2026-10-16"),
            (project["status"]!.GetValue<string>(), project["state"]!.GetValue<string>(), project["asOf"]!.GetValue<string>()));
        Assert.Equal(20_000m, Amount(project["money"]));
        Assert.Null(project["projected"]);
        // Done projects come last.
        Assert.Equal("Road to DE", response["projects"]!.AsArray()[^1]!["name"]!.GetValue<string>());

        foreach (var day in new[] { "2026-10-18", "2026-09-26" })
        {
            var refused = await Send(factory, HttpMethod.Put, $"/api/projects/{id}/status", sub, new JsonObject { ["status"] = "done", ["doneOn"] = day });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        // Reopen makes it Active again, with today's figures.
        var reopened = Project(await Json(await Send(factory, HttpMethod.Put, $"/api/projects/{id}/status", sub, new JsonObject { ["status"] = "active" })), "Road to DE");
        Assert.Equal(("active", 20_500m), (reopened["status"]!.GetValue<string>(), Amount(reopened["money"])));
    }

    // R-PRJ-01: Pause keeps the amounts and adds nothing more; Resume adds them again.
    [Fact]
    public async Task Pause_keeps_the_amounts_and_adds_nothing_more()
    {
        var sub = await SetUpWithMoney();
        var id = Id(await Post(sub, Request()), "Road to DE");

        var paused = Project(await Json(await Send(factory, HttpMethod.Put, $"/api/projects/{id}/status", sub, new JsonObject { ["status"] = "paused" })), "Road to DE");
        Assert.Equal(("paused", "paused", 2_000m),
            (paused["status"]!.GetValue<string>(), paused["state"]!.GetValue<string>(), Amount(paused["monthlyAmount"])));
        Assert.Equal(20_000m, Amount(paused["projected"]));

        var resumed = Project(await Json(await Send(factory, HttpMethod.Put, $"/api/projects/{id}/status", sub, new JsonObject { ["status"] = "active" })), "Road to DE");
        // 20,000 + 2,000 + 5 × 2,000 = 32,000.
        Assert.Equal(("active", 32_000m), (resumed["status"]!.GetValue<string>(), Amount(resumed["projected"])));
    }

    // R-PRJ-05: each month has its own amount, only from the start month to the deadline.
    [Fact]
    public async Task Each_month_has_its_own_amount()
    {
        var sub = await SetUpWithMoney();
        var id = Id(await Post(sub, Request()), "Road to DE");
        var amounts = PlanKeys.ToDictionary(key => key, key => string.CompareOrdinal(key, "2026-11") >= 0 ? 3_000m : 2_000m);

        var changed = Project(await Json(await Send(factory, HttpMethod.Put, $"/api/projects/{id}", sub, Request(amounts: amounts))), "Road to DE");
        Assert.Equal([2_000m, 2_000m, 3_000m, 3_000m, 3_000m, 3_000m], changed["plan"]!.AsArray().Select(month => Amount(month!["amount"])!.Value));
        // 20,000 + 2,000 + 2,000 (Oct) + 4 × 3,000 (Nov to Feb) = 36,000.
        Assert.Equal(36_000m, Amount(changed["projected"]));

        // An empty month is 0.
        var gap = Project(await Json(await Send(factory, HttpMethod.Put, $"/api/projects/{id}", sub,
            Request(amounts: new Dictionary<string, decimal> { ["2026-09"] = 2_000, ["2026-10"] = 0 }))), "Road to DE");
        Assert.Equal([2_000m, 0m, 0m, 0m, 0m, 0m], gap["plan"]!.AsArray().Select(month => Amount(month!["amount"])!.Value));

        foreach (var outside in new[] { "2026-08", "2027-03" })
        {
            var response = await Send(factory, HttpMethod.Put, $"/api/projects/{id}", sub, Request(amounts: new Dictionary<string, decimal> { [outside] = 1_000 }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("\"Monthly\"", await response.Content.ReadAsStringAsync());
        }
    }

    // PRJ-04, R-PRJ-12: the dialog's months and the recommended amount: 30,000 − 20,000 (Al Rajhi on
    // 27 Sep) over 6 months = 1,666.67, rounded up to 1,667.
    [Fact]
    public async Task The_dialog_gets_the_months_and_a_recommended_amount()
    {
        var sub = await SetUpWithMoney();

        var months = await Json(await Send(factory, HttpMethod.Get, "/api/projects/months?deadline=2027-03-15&bankAccountId=rajhi&likely=30000", sub));
        Assert.Equal(PlanKeys, months["months"]!.AsArray().Select(month => month!["key"]!.GetValue<string>()));
        Assert.Equal("2026-09", months["currentKey"]!.GetValue<string>());
        Assert.Equal(1_667m, Amount(months["recommended"]));

        var noTarget = await Json(await Send(factory, HttpMethod.Get, "/api/projects/months?deadline=2027-03-15&bankAccountId=rajhi", sub));
        Assert.Null(noTarget["recommended"]);
        var reached = await Json(await Send(factory, HttpMethod.Get, "/api/projects/months?deadline=2027-03-15&bankAccountId=rajhi&likely=15000", sub));
        Assert.Equal(0m, Amount(reached["recommended"]));

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(factory, HttpMethod.Get, "/api/projects/months?deadline=2077-01-01", sub)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Get, "/api/projects/months?deadline=2027-03-15&projectId=missing", sub)).StatusCode);
    }

    // R-PRJ-06, R-PRJ-12: an account opened inside the month counts from its opening day: 5,000 on
    // 10 Oct, 1,000 moved in on 12 Oct; recommended (30,000 − 5,000) ÷ 6 = 4,166.67, so 4,167.
    [Fact]
    public async Task An_account_opened_this_month_counts_from_its_opening_day()
    {
        var sub = await SetUpWithMoney();
        await factory.Store.AddBankAccountAsync(sub, new BankAccount("new", "New", Opening: new OpeningBalance(500_000, new DateOnly(2026, 10, 10))));
        await factory.Store.AddTransferAsync(sub, new Transfer("t1", new DateOnly(2026, 10, 12), "2026-09", "2026-01", "bank", "new", 100_000, ""));

        var project = Project(await Post(sub, Request(account: "new", milestones: [])), "Road to DE");
        Assert.Equal((1_000m, 1_000m), (Amount(project["months"]![0]!["actual"]), Amount(project["stillToPutIn"])));

        var months = await Json(await Send(factory, HttpMethod.Get, "/api/projects/months?deadline=2027-03-15&bankAccountId=new&likely=30000", sub));
        Assert.Equal(4_167m, Amount(months["recommended"]));
    }

    // R-PRJ-01: deleting a project changes no balance or expense.
    [Fact]
    public async Task Delete_removes_only_the_project()
    {
        var sub = await SetUpWithMoney();
        var id = Id(await Post(sub, Request()), "Road to DE");
        await Expense(sub, "bank", 50);

        var response = await Send(factory, HttpMethod.Delete, $"/api/projects/{id}", sub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await Json(response))["projects"]!.AsArray());
        Assert.Single(factory.Store.Entries(sub));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Delete, $"/api/projects/{id}", sub)).StatusCode);
    }

    // R-PRJ-01 to R-PRJ-08 and PRD 4.7.
    [Fact]
    public async Task Invalid_projects_are_refused()
    {
        var sub = await SetUpWithMoney();
        await Post(sub, Request());
        await factory.Store.UpdateBankAccountAsync(sub, new BankAccount("alinma", "Alinma", Archived: true, Opening: new OpeningBalance(800_000, FyStart)));

        var cases = new (JsonObject Body, string Field)[]
        {
            (Request(name: "road to de", account: "bank"), "Name"),
            (Request(name: " "), "Name"),
            (Request(best: null, likely: null), "Targets"),
            (Request(active: "worst"), "ActiveTarget"),
            (Request(name: "Car", account: ""), "BankAccountId"),
            (Request(name: "Car", account: "missing"), "BankAccountId"),
            (Request(name: "Car", account: "alinma"), "BankAccountId"),
            (Request(name: "Car", account: "bank", likely: 0), "Likely"),
            (Request(name: "Car", account: "bank", monthly: -1), "Monthly[0].Amount"),
            (Request(name: "Car", account: "bank", deadline: "2077-01-01"), "Deadline"),
            (Request(name: "Car", account: "bank", milestones: new JsonArray(new JsonObject { ["label"] = "", ["date"] = "2026-12-01", ["amount"] = 1 })), "Milestones[0].Label"),
        };
        foreach (var (body, field) in cases)
        {
            var response = await Send(factory, HttpMethod.Post, "/api/projects", sub, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync());
        }
    }

    // R-PRJ-03: until an account has an opening balance, nothing is known.
    [Fact]
    public async Task Without_an_opening_balance_the_project_is_unknown()
    {
        var sub = await SetUpUser(factory);
        await Post(sub, Request(account: "bank"));

        var project = Project(await Get(sub), "Road to DE");

        Assert.Equal("unknown", project["state"]!.GetValue<string>());
        Assert.Null(project["progress"]);
        Assert.Null(project["projected"]);
    }

    // Another user's project is 404.
    [Fact]
    public async Task Other_users_cannot_touch_a_project()
    {
        var sub = await SetUpWithMoney();
        var id = Id(await Post(sub, Request()), "Road to DE");

        var stranger = await SetUpWithMoney();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Put, $"/api/projects/{id}", stranger, Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Delete, $"/api/projects/{id}", stranger)).StatusCode);
        Assert.Empty((await Get(stranger))["projects"]!.AsArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/projects")).StatusCode);
    }
}
