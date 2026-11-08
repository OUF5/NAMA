using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using static Api.Tests.ApiCalls;
using static Api.Tests.ConnectorCalls;

namespace Api.Tests;

// MCP-01 to MCP-05: Claude Code connects through Connect, gets read-only tools, and can be disconnected.
// Today is 17 Oct 2026, 12:00 in Riyadh; FY 2026 runs 27 Jan 2026 to 26 Jan 2027 (payday 27).
public class ConnectorTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory factory;

    public ConnectorTests(ApiFactory factory)
    {
        this.factory = factory;
        factory.Time.UtcNow = TestTime.Default;
    }

    private static string NewEmail() => $"friend-{Guid.NewGuid():N}@example.com";

    // MCP-02
    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/oauth-protected-resource/api/mcp")]
    public async Task The_resource_metadata_names_NAMA_as_its_authorization_server(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(Resource, body["resource"]!.GetValue<string>());
        Assert.Equal([ApiFactory.Origin], body["authorization_servers"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal(["nama.read"], body["scopes_supported"]!.AsArray().Select(item => item!.GetValue<string>()));
    }

    [Fact]
    public async Task The_authorization_server_metadata_offers_code_with_S256_and_registration()
    {
        var body = await Json(await factory.CreateClient().GetAsync("/.well-known/oauth-authorization-server"));

        Assert.Equal(ApiFactory.Origin, body["issuer"]!.GetValue<string>());
        Assert.Equal("http://localhost/connect", body["authorization_endpoint"]!.GetValue<string>());
        Assert.Equal("http://localhost/api/oauth/token", body["token_endpoint"]!.GetValue<string>());
        Assert.Equal("http://localhost/api/oauth/register", body["registration_endpoint"]!.GetValue<string>());
        Assert.Equal(["S256"], body["code_challenge_methods_supported"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal(["none"], body["token_endpoint_auth_methods_supported"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal(["authorization_code", "refresh_token"], body["grant_types_supported"]!.AsArray().Select(item => item!.GetValue<string>()));
    }

    [Fact]
    public async Task Without_a_token_the_MCP_server_says_where_to_sign_in()
    {
        var response = await McpSend(factory, null, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "Bearer resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/api/mcp\", scope=\"nama.read\"",
            response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task An_unknown_token_is_refused_as_invalid()
    {
        var response = await McpSend(factory, "made-up-token", "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("error=\"invalid_token\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Registration_answers_a_public_client_and_stores_nothing()
    {
        var response = await Register(factory);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(("Claude Code (nama)", "none"),
            (body["client_name"]!.GetValue<string>(), body["token_endpoint_auth_method"]!.GetValue<string>()));
        Assert.Equal([Callback], body["redirect_uris"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal(OAuth.ReadClientId(body["client_id"]!.GetValue<string>())!.RedirectUris, [Callback]);
        Assert.Empty(factory.Connections.Codes);
    }

    [Theory]
    [InlineData("https://evil.example/callback")]
    [InlineData("http://localhost:3000/oauth/callback")]
    public async Task Registration_refuses_a_callback_that_is_not_Claudes(string callback)
    {
        Assert.Equal("invalid_redirect_uri", await OAuthError(await Register(factory, callback)));
    }

    // MCP-01: what Connect shows before Allow.
    [Fact]
    public async Task Connect_shows_the_app_and_where_it_sends_you()
    {
        var sub = await SetUpUser(factory);
        var clientId = await ClientId(factory);

        var response = await Send(factory, HttpMethod.Post, "/api/oauth/check", sub, Request(clientId, Pkce.New()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(("Claude Code (nama)", "localhost:33418", "nama.read"),
            (body["clientName"]!.GetValue<string>(), body["redirectHost"]!.GetValue<string>(), body["scope"]!.GetValue<string>()));
    }

    public static TheoryData<string, string, JsonNode?> InvalidRequests => new()
    {
        { "clientId", "not registered", "someone-else" },
        { "redirectUri", "did not register", "http://localhost:9999/callback" },
        { "redirectUri", "did not register", null },
        { "responseType", "kind of sign-in", "token" },
        { "codeChallengeMethod", "PKCE", "plain" },
        { "codeChallenge", "PKCE", null },
        { "resource", "something other than NAMA", "https://evil.example/mcp" },
    };

    // Shown on the page; nothing sends you anywhere, and no code is made.
    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Connect_refuses_a_request_that_fails_a_check(string field, string message, JsonNode? value)
    {
        var sub = await SetUpUser(factory);
        var request = Request(await ClientId(factory), Pkce.New());
        request[field] = value;

        foreach (var path in new[] { "/api/oauth/check", "/api/oauth/authorize" })
        {
            var response = await Send(factory, HttpMethod.Post, path, sub, request.DeepClone());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(message, (await Json(response))["detail"]!.GetValue<string>());
        }
        Assert.DoesNotContain(factory.Connections.Codes, code => code.Uid == sub);
    }

    [Fact]
    public async Task A_stranger_cannot_allow()
    {
        var request = Request(await ClientId(factory), Pkce.New());

        var check = await Send(factory, HttpMethod.Post, "/api/oauth/check", NewSub(), request, email: NewEmail());
        var allow = await Send(factory, HttpMethod.Post, "/api/oauth/authorize", NewSub(), request, email: NewEmail());

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (check.StatusCode, allow.StatusCode));
    }

    [Fact]
    public async Task Cancel_sends_access_denied_with_the_state()
    {
        var sub = await SetUpUser(factory);

        var response = await Send(factory, HttpMethod.Post, "/api/oauth/authorize", sub, Request(await ClientId(factory), Pkce.New(), allow: false));

        var redirect = new Uri((await Json(response))["redirectTo"]!.GetValue<string>());
        Assert.Equal("http://localhost:33418/callback", redirect.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(redirect.Query);
        Assert.Equal(("access_denied", "state-123", false), (query["error"].ToString(), query["state"].ToString(), query.ContainsKey("code")));
    }

    // MCP-03
    [Fact]
    public async Task Allow_gives_a_5_minute_code_that_only_its_hash_is_stored_for()
    {
        var sub = await SetUpUser(factory);
        var clientId = await ClientId(factory);

        var code = await Allow(factory, sub, clientId, Pkce.New());

        var stored = factory.Connections.Codes.Single(item => item.Uid == sub);
        Assert.Equal(OAuth.Hash(code), stored.Id);
        Assert.Equal((ApiFactory.AllowedEmail, clientId, Callback, TestTime.Default.AddMinutes(5)),
            (stored.Email, stored.ClientId, stored.RedirectUri, stored.ExpiresAt));
    }

    [Fact]
    public async Task The_code_becomes_tokens_and_the_tokens_reach_the_tools()
    {
        var sub = await SetUpUser(factory);

        var tokens = await Connect(factory, sub);

        var connection = factory.Connections.Connections.Single(item => item.Uid == sub);
        Assert.Equal((OAuth.Hash(tokens.AccessToken), OAuth.Hash(tokens.RefreshToken)), (connection.AccessTokenHash, connection.RefreshTokenHash));
        Assert.Equal((TestTime.Default.AddHours(1), TestTime.Default.AddDays(30)), (connection.AccessExpiresAt, connection.RefreshExpiresAt));
        Assert.Equal(("Claude Code (nama)", "nama.read"), (connection.ClientName, connection.Scope));
        Assert.NotNull(await CallTool(factory, tokens.AccessToken, "get_calendar"));
    }

    public static TheoryData<string, string> WrongExchanges => new()
    {
        { "code_verifier", Pkce.New().Verifier },
        { "code_verifier", "" },
        { "redirect_uri", "http://localhost:9999/callback" },
        { "client_id", "nama_other" },
        { "resource", "https://evil.example/mcp" },
    };

    [Theory]
    [MemberData(nameof(WrongExchanges))]
    public async Task A_code_with_a_wrong_verifier_redirect_client_or_resource_is_refused(string field, string value)
    {
        var sub = await SetUpUser(factory);
        var clientId = await ClientId(factory);
        var pkce = Pkce.New();
        var form = CodeForm(await Allow(factory, sub, clientId, pkce), clientId, pkce);
        form[field] = value;

        Assert.Equal("invalid_grant", await OAuthError(await Token(factory, form)));
        // The code is used up even so.
        Assert.Equal("invalid_grant", await OAuthError(await Token(factory, CodeForm(form["code"], clientId, pkce))));
        Assert.DoesNotContain(factory.Connections.Connections, connection => connection.Uid == sub);
    }

    [Fact]
    public async Task A_code_works_once()
    {
        var sub = await SetUpUser(factory);
        var clientId = await ClientId(factory);
        var pkce = Pkce.New();
        var form = CodeForm(await Allow(factory, sub, clientId, pkce), clientId, pkce);

        await ReadTokens(await Token(factory, form), clientId);

        Assert.Equal("invalid_grant", await OAuthError(await Token(factory, form)));
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.OK)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    public async Task A_code_expires_after_5_minutes(int secondsAfterExpiry, HttpStatusCode expected)
    {
        var sub = await SetUpUser(factory);
        var clientId = await ClientId(factory);
        var pkce = Pkce.New();
        var code = await Allow(factory, sub, clientId, pkce);
        factory.Time.UtcNow = TestTime.Default.AddMinutes(5).AddSeconds(secondsAfterExpiry);

        Assert.Equal(expected, (await Token(factory, CodeForm(code, clientId, pkce))).StatusCode);
    }

    [Fact]
    public async Task The_token_endpoint_needs_a_known_grant_sent_as_a_form()
    {
        var unknown = await Token(factory, new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
        var asJson = await factory.CreateClient().PostAsync("/api/oauth/token",
            System.Net.Http.Json.JsonContent.Create(new JsonObject { ["grant_type"] = "refresh_token" }));

        Assert.Equal("unsupported_grant_type", await OAuthError(unknown));
        Assert.Equal("invalid_request", await OAuthError(asJson));
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.OK)]
    [InlineData(0, HttpStatusCode.Unauthorized)]
    public async Task An_access_token_lasts_1_hour(int secondsAfterExpiry, HttpStatusCode expected)
    {
        var tokens = await Connect(factory, await SetUpUser(factory));
        factory.Time.UtcNow = TestTime.Default.AddHours(1).AddSeconds(secondsAfterExpiry);

        Assert.Equal(expected, (await McpSend(factory, tokens.AccessToken, "tools/list")).StatusCode);
    }

    [Fact]
    public async Task A_refresh_gives_a_new_pair_and_the_old_ones_stop_working()
    {
        var sub = await SetUpUser(factory);
        var tokens = await Connect(factory, sub);
        factory.Time.UtcNow = TestTime.Default.AddHours(2);

        var refreshed = await ReadTokens(await Token(factory, RefreshForm(tokens)), tokens.ClientId);

        Assert.Equal(HttpStatusCode.OK, (await McpSend(factory, refreshed.AccessToken, "tools/list")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await McpSend(factory, tokens.AccessToken, "tools/list")).StatusCode);
        Assert.Equal("invalid_grant", await OAuthError(await Token(factory, RefreshForm(tokens))));
        var connection = factory.Connections.Connections.Single(item => item.Uid == sub);
        Assert.Equal((TestTime.Default.AddHours(2), TestTime.Default.AddHours(2).AddDays(30)), (connection.LastUsedAt, connection.RefreshExpiresAt));
    }

    [Fact]
    public async Task A_refresh_token_works_only_for_its_app_and_for_30_days()
    {
        var tokens = await Connect(factory, await SetUpUser(factory));

        Assert.Equal("invalid_grant", await OAuthError(await Token(factory, RefreshForm(tokens with { ClientId = "nama_other" }))));
        factory.Time.UtcNow = TestTime.Default.AddDays(30);
        Assert.Equal("invalid_grant", await OAuthError(await Token(factory, RefreshForm(tokens))));
    }

    // MCP-03: each token works only on its own side.
    [Fact]
    public async Task Claudes_token_is_refused_by_the_app_API_and_a_Firebase_token_by_the_MCP_server()
    {
        var sub = await SetUpUser(factory);
        var tokens = await Connect(factory, sub);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/summary");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var app = await factory.CreateClient().SendAsync(request);
        var mcp = await McpSend(factory, TestTokens.Create(sub: sub), "tools/list");

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (app.StatusCode, mcp.StatusCode));
    }

    // AUTH-02 on every call: a removed person's app stops at once.
    [Fact]
    public async Task A_removed_persons_app_is_refused()
    {
        var (sub, email) = (NewSub(), NewEmail());
        var memberId = await Join(factory, sub, email);
        var tokens = await Connect(factory, sub, email);
        Assert.Equal(HttpStatusCode.OK, (await McpSend(factory, tokens.AccessToken, "tools/list")).StatusCode);

        await Send(factory, HttpMethod.Delete, $"/api/people/{memberId}", NewSub());

        Assert.Equal(HttpStatusCode.Forbidden, (await McpSend(factory, tokens.AccessToken, "tools/list")).StatusCode);
    }

    // MCP-04
    [Fact]
    public async Task Every_tool_is_marked_read_only()
    {
        var tokens = await Connect(factory, await SetUpUser(factory));

        var tools = (await McpResult(await McpSend(factory, tokens.AccessToken, "tools/list")))["tools"]!.AsArray();

        Assert.Equal(
            ["get_balances", "get_banks", "get_briefing", "get_calendar", "get_categories", "get_income", "get_loans", "get_month",
                "get_overview", "get_plans", "get_projects", "get_summary", "list_entries"],
            tools.Select(tool => tool!["name"]!.GetValue<string>()).Order());
        Assert.All(tools, tool =>
        {
            var hints = tool!["annotations"]!;
            Assert.Equal((true, false), (hints["readOnlyHint"]!.GetValue<bool>(), hints["destructiveHint"]!.GetValue<bool>()));
        });
    }

    // The same LoadAsync answers both, so Claude and the app agree. Nulls are left out of a tool's answer.
    [Theory]
    [InlineData("get_summary", "/api/summary")]
    [InlineData("get_categories", "/api/categories")]
    [InlineData("get_banks", "/api/banks")]
    [InlineData("get_loans", "/api/loans")]
    [InlineData("get_income", "/api/income")]
    [InlineData("get_overview", "/api/fiscal-years/2026-01/overview")]
    [InlineData("get_month", "/api/fiscal-years/2026-01/months/2026-09")]
    [InlineData("get_plans", "/api/fiscal-years/2026-01/plans")]
    [InlineData("get_balances", "/api/fiscal-years/2026-01/balances")]
    [InlineData("get_projects", "/api/projects")]
    public async Task A_tool_answers_what_its_page_reads(string tool, string url)
    {
        var sub = await SetUpWithData();
        var tokens = await Connect(factory, sub);

        var page = await Json(await Send(factory, HttpMethod.Get, url, sub));
        var answer = ToolJson(await CallTool(factory, tokens.AccessToken, tool));

        Assert.Equal(WithoutNulls(page)!.ToJsonString(), WithoutNulls(answer)!.ToJsonString());
    }

    // Owner note 34: get_month takes Tracker's months back (?back=) for the overspend to make up.
    [Fact]
    public async Task get_month_counts_the_overspend_over_the_months_back()
    {
        var sub = await SetUpWithData();
        var tokens = await Connect(factory, sub);

        var page = await Json(await Send(factory, HttpMethod.Get, "/api/fiscal-years/2026-01/months/2026-09?back=3", sub));
        var answer = ToolJson(await CallTool(factory, tokens.AccessToken, "get_month", new JsonObject { ["back"] = 3 }));

        Assert.Equal(WithoutNulls(page)!.ToJsonString(), WithoutNulls(answer)!.ToJsonString());
        Assert.Equal(3, answer["overspent"]!["monthsBack"]!.GetValue<int>());
    }

    [Fact]
    public async Task get_calendar_answers_me_and_the_fiscal_years()
    {
        var sub = await SetUpWithData();
        var tokens = await Connect(factory, sub);

        var answer = ToolJson(await CallTool(factory, tokens.AccessToken, "get_calendar"));

        Assert.Equal(WithoutNulls(await Json(await Send(factory, HttpMethod.Get, "/api/me", sub)))!.ToJsonString(), answer["me"]!.ToJsonString());
        Assert.Equal(WithoutNulls(await Json(await Send(factory, HttpMethod.Get, "/api/fiscal-years", sub)))!.ToJsonString(),
            answer["fiscalYears"]!.ToJsonString());
    }

    // Owner note 34 (TBL-07): every type, newest first, with names, as the Entries page lists them, and
    // each type's total; the filters as the page's.
    [Fact]
    public async Task list_entries_answers_every_type_with_names_and_totals()
    {
        var sub = await SetUpWithData();
        await factory.Store.AddTransferAsync(sub, new Transfer("t1", new DateOnly(2026, 10, 14), "2026-09", "2026-01", "bank", "rajhi", 25_000, "Card"));
        await factory.Store.AddIncomeAsync(sub, new IncomeItem("gift", IncomeKind.Additional, "Gift", 50_000, "2026-09", "2026-09", IncomeCounts.Actual, "rajhi"));
        var tokens = await Connect(factory, sub);

        var answer = ToolJson(await CallTool(factory, tokens.AccessToken, "list_entries"));

        Assert.Equal((4, false, 200m, 250m, 500m, "2026-01"), (
            answer["count"]!.GetValue<int>(), answer["truncated"]!.GetValue<bool>(), answer["spent"]!.GetValue<decimal>(),
            answer["moved"]!.GetValue<decimal>(), answer["income"]!.GetValue<decimal>(), answer["fyId"]!.GetValue<string>()));
        var rows = answer["entries"]!.AsArray();
        Assert.Equal(["transfer", "expense", "expense", "income"], rows.Select(row => row!["type"]!.GetValue<string>()));
        Assert.Equal(("SNB", "Al Rajhi", 250m, "Oct 2026"), (
            rows[0]!["fromAccount"]!.GetValue<string>(), rows[0]!["toAccount"]!.GetValue<string>(),
            rows[0]!["amount"]!.GetValue<decimal>(), rows[0]!["month"]!.GetValue<string>()));
        var lunch = rows.Single(row => row!["note"]?.GetValue<string>() == "Lunch with Sara")!;
        Assert.Equal(("Food", "Groceries", "SNB", "groceries", 40m), (
            lunch["category"]!.GetValue<string>(), lunch["subCategory"]!.GetValue<string>(), lunch["account"]!.GetValue<string>(),
            lunch["subCategoryId"]!.GetValue<string>(), lunch["amount"]!.GetValue<decimal>()));
        Assert.Equal(("Gift", "actual", "Al Rajhi", "2026-09-27"), (
            rows[3]!["label"]!.GetValue<string>(), rows[3]!["counts"]!.GetValue<string>(), rows[3]!["account"]!.GetValue<string>(),
            rows[3]!["date"]!.GetValue<string>()));

        var lunchOnly = ToolJson(await CallTool(factory, tokens.AccessToken, "list_entries", new JsonObject { ["q"] = "lunch" }));
        Assert.Equal((1, 40m), (lunchOnly["count"]!.GetValue<int>(), lunchOnly["spent"]!.GetValue<decimal>()));
        var transfers = ToolJson(await CallTool(factory, tokens.AccessToken, "list_entries", new JsonObject { ["type"] = "transfer" }));
        Assert.Equal(["transfer"], transfers["entries"]!.AsArray().Select(row => row!["type"]!.GetValue<string>()));
        var wrong = await CallTool(factory, tokens.AccessToken, "list_entries", new JsonObject { ["type"] = "loan" });
        Assert.True(wrong["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task list_entries_sends_the_newest_300_and_counts_them_all()
    {
        var sub = await SetUpUser(factory);
        for (var i = 0; i < 301; i++)
        {
            await factory.Store.AddEntryAsync(sub, new Entry(
                $"e{i:000}", new DateOnly(2026, 10, 1).AddDays(i % 16), "2026-09", "2026-01", "food", "groceries", "bank", 100, ""));
        }
        var tokens = await Connect(factory, sub);

        var answer = ToolJson(await CallTool(factory, tokens.AccessToken, "list_entries"));

        Assert.Equal((301, 301m, true, 300),
            (answer["count"]!.GetValue<int>(), answer["spent"]!.GetValue<decimal>(), answer["truncated"]!.GetValue<bool>(), answer["entries"]!.AsArray().Count));
    }

    // Owner note 34: get_briefing, "start here". Today is 17 Oct 2026, in Oct 2026 (2026-09, 27 Sep to 26
    // Oct). Groceries: 200 of its 1,000 this month. Fuel: 900 of its 1,000 (near limit), and 30 with no
    // plan last month, so Transport has 30 to make up. A Bonus of 300 comes next month. Names, never
    // only ids, and the month's numbers are the top bar's.
    [Fact]
    public async Task get_briefing_says_how_the_month_and_the_year_are_going_with_names()
    {
        var sub = await SetUpWithData();
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("fuel", "transport", "Fuel", 0));
        await factory.Store.SetPlanAsync(sub, "2026-01", new PlanAmounts("fuel", "transport", new Dictionary<string, long> { ["2026-09"] = 100_000 }));
        await factory.Store.AddEntryAsync(sub, new Entry("f1", new DateOnly(2026, 10, 2), "2026-09", "2026-01", "transport", "fuel", "rajhi", 90_000, "Road trip"));
        await factory.Store.AddEntryAsync(sub, new Entry("f0", new DateOnly(2026, 9, 2), "2026-08", "2026-01", "transport", "fuel", "rajhi", 3_000, ""));
        await factory.Store.AddIncomeAsync(sub, new IncomeItem("bonus", IncomeKind.Additional, "Bonus", 30_000, "2026-10", "2026-10", IncomeCounts.Both, "rajhi"));
        var tokens = await Connect(factory, sub);

        var briefing = ToolJson(await CallTool(factory, tokens.AccessToken, "get_briefing"));
        var summary = (await Json(await Send(factory, HttpMethod.Get, "/api/summary", sub)))["month"]!;

        Assert.Equal(("2026-10-17", "SAR", "FY 2026", "2026-01"), (
            briefing["today"]!.GetValue<string>(), briefing["currency"]!.GetValue<string>(),
            briefing["fiscalYear"]!.GetValue<string>(), briefing["fyId"]!.GetValue<string>()));
        var month = briefing["month"]!;
        Assert.Equal(("2026-09", 21, 10), (month["period"]!["key"]!.GetValue<string>(), month["day"]!.GetValue<int>(), month["daysLeft"]!.GetValue<int>()));
        Assert.Equal((summary["planned"]!.GetValue<decimal>(), summary["spent"]!.GetValue<decimal>()), (month["planned"]!.GetValue<decimal>(), month["spent"]!.GetValue<decimal>()));
        Assert.Equal((2_000m, 1_100m, 900m, "withinPlan", 30m, 870m, 10_000m), (
            month["planned"]!.GetValue<decimal>(), month["spent"]!.GetValue<decimal>(), month["remainingThisMonth"]!.GetValue<decimal>(),
            month["status"]!.GetValue<string>(), month["overspentLastMonth"]!.GetValue<decimal>(),
            month["remainingAfterOverspent"]!.GetValue<decimal>(), month["income"]!.GetValue<decimal>()));

        var watch = briefing["watch"]!.AsArray().Single()!;
        Assert.Equal(("Transport", "Fuel", 900m, 1_000m, "nearLimit"), (
            watch["category"]!.GetValue<string>(), watch["subCategory"]!.GetValue<string>(), watch["spent"]!.GetValue<decimal>(),
            watch["planned"]!.GetValue<decimal>(), watch["status"]!.GetValue<string>()));
        Assert.Equal(["Transport", "Food"], briefing["topCategories"]!.AsArray().Select(line => line!["category"]!.GetValue<string>()));
        Assert.Equal(30m, briefing["topCategories"]![0]!["lastMonthSpent"]!.GetValue<decimal>());
        var overspent = briefing["overspentToMakeUp"]!.AsArray().Single()!;
        Assert.Equal(("Transport", 30m, 100m, 70m), (
            overspent["category"]!.GetValue<string>(), overspent["overspent"]!.GetValue<decimal>(),
            overspent["leftThisMonth"]!.GetValue<decimal>(), overspent["afterCovering"]!.GetValue<decimal>()));
        Assert.Equal(("Road trip", "Fuel", "Al Rajhi", 900m), (
            briefing["biggestThisMonth"]![0]!["note"]!.GetValue<string>(), briefing["biggestThisMonth"]![0]!["subCategory"]!.GetValue<string>(),
            briefing["biggestThisMonth"]![0]!["account"]!.GetValue<string>(), briefing["biggestThisMonth"]![0]!["amount"]!.GetValue<decimal>()));
        Assert.Equal(3, briefing["latestThisMonth"]!.AsArray().Count);

        var bonus = briefing["additionalIncomeSoon"]!.AsArray().Single()!;
        Assert.Equal(("Bonus", "Nov 2026", 300m, "both", "Al Rajhi"), (
            bonus["label"]!.GetValue<string>(), bonus["month"]!.GetValue<string>(), bonus["amount"]!.GetValue<decimal>(),
            bonus["counts"]!.GetValue<string>(), bonus["account"]!.GetValue<string>()));
        Assert.Equal((500m, 1), (briefing["loans"]!["lentOpen"]!.GetValue<decimal>(), briefing["loans"]!["lentOpenCount"]!.GetValue<int>()));
        Assert.False(briefing["money"]!["complete"]!.GetValue<bool>());
        Assert.Equal(["Al Rajhi", "SNB"], briefing["money"]!["accounts"]!.AsArray().Select(account => account!["name"]!.GetValue<string>()));
        var outlook = (await Json(await Send(factory, HttpMethod.Get, "/api/fiscal-years/2026-01/overview", sub)))["planVsActual"]!;
        Assert.Equal((outlook["spent"]!.GetValue<decimal>(), outlook["projectedSaved"]!.GetValue<decimal>()), (
            briefing["outlook"]!["spentSoFar"]!.GetValue<decimal>(), briefing["outlook"]!["projectedSavingAtFyEnd"]!.GetValue<decimal>()));
        Assert.Empty(briefing["projects"]!.AsArray());
    }

    // Owner note 34: compact whatever the year holds: under 40,000 characters (about 10,000 tokens,
    // where Claude Code warns) with 600 expenses over 30 sub-categories.
    [Fact]
    public async Task get_briefing_stays_compact_in_a_busy_year()
    {
        var sub = await SetUpUser(factory);
        for (var s = 0; s < 30; s++)
        {
            await factory.Store.AddSubCategoryAsync(sub, new SubCategory($"sub{s}", s % 2 == 0 ? "food" : "transport", $"Sub-category number {s}", s));
            await factory.Store.SetPlanAsync(sub, "2026-01", new PlanAmounts($"sub{s}", s % 2 == 0 ? "food" : "transport",
                new Dictionary<string, long> { ["2026-08"] = 10_000, ["2026-09"] = 10_000 }));
        }
        for (var i = 0; i < 600; i++)
        {
            var (date, key) = i % 2 == 0 ? (new DateOnly(2026, 10, 1 + i % 16), "2026-09") : (new DateOnly(2026, 9, 1 + i % 20), "2026-08");
            await factory.Store.AddEntryAsync(sub, new Entry($"e{i}", date, key, "2026-01", i % 60 < 30 ? "food" : "transport", $"sub{i % 30}", "bank", 900 + i, $"Entry number {i} with a note"));
        }
        var tokens = await Connect(factory, sub);

        var result = await CallTool(factory, tokens.AccessToken, "get_briefing");

        Assert.InRange(result["content"]![0]!["text"]!.GetValue<string>().Length, 1, 40_000);
        Assert.Equal(5, ToolJson(result)["watch"]!.AsArray().Count);
    }

    // MCP-07 (owner note 34): ready-made prompts that point Claude at the tools.
    [Fact]
    public async Task Prompts_are_listed_and_name_the_tools_to_call()
    {
        var tokens = await Connect(factory, await SetUpUser(factory));

        var prompts = (await McpResult(await McpSend(factory, tokens.AccessToken, "prompts/list")))["prompts"]!.AsArray();
        Assert.Equal(["monthly_review", "savings_outlook", "spending_check"], prompts.Select(prompt => prompt!["name"]!.GetValue<string>()).Order());

        var review = await McpResult(await McpSend(factory, tokens.AccessToken, "prompts/get",
            new JsonObject { ["name"] = "monthly_review", ["arguments"] = new JsonObject { ["month"] = "Nov 2026" } }));
        var text = review["messages"]![0]!["content"]!["text"]!.GetValue<string>();
        Assert.Contains("the month Nov 2026", text);
        Assert.Contains("get_briefing", text);
        var check = await McpResult(await McpSend(factory, tokens.AccessToken, "prompts/get",
            new JsonObject { ["name"] = "spending_check", ["arguments"] = new JsonObject { ["category"] = "Groceries" } }));
        Assert.Contains("\"Groceries\"", check["messages"]![0]!["content"]!["text"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await McpSend(factory, null, "prompts/list")).StatusCode);
    }

    // Owner note 34: what Claude reads when it connects starts it at get_briefing and says the app's words.
    [Fact]
    public async Task The_instructions_start_at_get_briefing()
    {
        var tokens = await Connect(factory, await SetUpUser(factory));

        var init = await McpResult(await McpSend(factory, tokens.AccessToken, "initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-11-25",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "1" },
        }));

        var instructions = init["instructions"]!.GetValue<string>();
        Assert.Contains("Start with get_briefing", instructions);
        Assert.Contains("Remaining this month", instructions);
        Assert.NotNull(init["capabilities"]!["prompts"]);
    }

    [Fact]
    public async Task An_unknown_fiscal_year_is_a_tool_error_that_says_where_to_look()
    {
        var tokens = await Connect(factory, await SetUpUser(factory));

        var result = await CallTool(factory, tokens.AccessToken, "get_overview", new JsonObject { ["fyId"] = "1999-01" });

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("get_calendar", result["content"]![0]!["text"]!.GetValue<string>());
    }

    // MCP-05, STG-08
    [Fact]
    public async Task Settings_lists_the_connections_and_Disconnect_stops_the_app()
    {
        var sub = await SetUpUser(factory);
        var tokens = await Connect(factory, sub);
        factory.Time.UtcNow = TestTime.Default.AddDays(2);
        await ReadTokens(await Token(factory, RefreshForm(tokens)), tokens.ClientId);

        var listed = (await Json(await Send(factory, HttpMethod.Get, "/api/settings", sub)))["connections"]!.AsArray().Single()!;

        Assert.Equal(("Claude Code (nama)", "2026-10-17", "2026-10-19"),
            (listed["clientName"]!.GetValue<string>(), listed["connectedOn"]!.GetValue<string>(), listed["lastActiveOn"]!.GetValue<string>()));
        var disconnected = await Send(factory, HttpMethod.Delete, $"/api/connections/{listed["id"]!.GetValue<string>()}", sub);
        Assert.Equal(HttpStatusCode.OK, disconnected.StatusCode);
        Assert.Empty((await Json(disconnected))["connections"]!.AsArray());
        Assert.DoesNotContain(factory.Connections.Connections, connection => connection.Uid == sub);
    }

    [Fact]
    public async Task After_Disconnect_neither_token_works()
    {
        var sub = await SetUpUser(factory);
        var tokens = await Connect(factory, sub);
        var id = factory.Connections.Connections.Single(connection => connection.Uid == sub).Id;

        await Send(factory, HttpMethod.Delete, $"/api/connections/{id}", sub);

        Assert.Equal(HttpStatusCode.Unauthorized, (await McpSend(factory, tokens.AccessToken, "tools/list")).StatusCode);
        Assert.Equal("invalid_grant", await OAuthError(await Token(factory, RefreshForm(tokens))));
    }

    [Fact]
    public async Task Someone_elses_connection_cannot_be_disconnected()
    {
        var owner = await SetUpUser(factory);
        await Connect(factory, owner);
        var id = factory.Connections.Connections.Single(connection => connection.Uid == owner).Id;

        var response = await Send(factory, HttpMethod.Delete, $"/api/connections/{id}", await SetUpUser(factory));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(factory.Connections.Connections, connection => connection.Id == id);
    }

    [Fact]
    public async Task A_connection_whose_refresh_token_expired_is_removed()
    {
        var sub = await SetUpUser(factory);
        await Connect(factory, sub);
        factory.Time.UtcNow = TestTime.Default.AddDays(30);

        var connections = (await Json(await Send(factory, HttpMethod.Get, "/api/settings", sub)))["connections"]!.AsArray();

        Assert.Empty(connections);
        Assert.DoesNotContain(factory.Connections.Connections, connection => connection.Uid == sub);
    }

    // Food › Groceries planned 1,000 a month, two entries in Oct 2026 (one with "lunch" in its note),
    // a salary of 10,000 and a loan.
    private async Task<string> SetUpWithData()
    {
        var sub = await SetUpUser(factory, incomes: [new IncomeItem("salary", IncomeKind.Salary, "Salary", 1_000_000, "2026-01", null)]);
        await factory.Store.AddSubCategoryAsync(sub, new SubCategory("groceries", "food", "Groceries", 0));
        await Send(factory, HttpMethod.Put, "/api/fiscal-years/2026-01/plans/groceries", sub, new JsonObject
        {
            ["amounts"] = new JsonObject { ["2026-09"] = 1000, ["2026-10"] = 1000 },
        });
        foreach (var (amount, note) in new[] { (40m, "Lunch with Sara"), (160m, "Weekly shop") })
        {
            var entry = await Send(factory, HttpMethod.Post, "/api/entries", sub, new JsonObject
            {
                ["subCategoryId"] = "groceries", ["bankAccountId"] = "bank", ["date"] = "2026-10-12", ["amount"] = amount, ["note"] = note,
            });
            Assert.Equal(HttpStatusCode.Created, entry.StatusCode);
        }
        var loan = await Send(factory, HttpMethod.Post, "/api/loans", sub, new JsonObject
        {
            ["name"] = "Ahmed", ["type"] = "lent", ["amount"] = 500, ["date"] = "2026-10-01", ["bankAccountId"] = "bank",
        });
        Assert.Equal(HttpStatusCode.Created, loan.StatusCode);
        return sub;
    }

    private static JsonNode? WithoutNulls(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Where(pair => pair.Value is not null)
            .Select(pair => KeyValuePair.Create(pair.Key, WithoutNulls(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(WithoutNulls).ToArray()),
        null => null,
        _ => node.DeepClone(),
    };
}

// MCP-06: with no PUBLIC_ORIGIN the connector does not exist. Signed in, so the answer is 404 (without a
// token every unknown path is 401, as for any route of the app).
public class ConnectorOffFactory : ApiFactory
{
    protected override string? PublicOrigin => null;
}

public class ConnectorOffTests(ConnectorOffFactory factory) : IClassFixture<ConnectorOffFactory>
{
    [Theory]
    [InlineData("/.well-known/oauth-authorization-server")]
    [InlineData("/.well-known/oauth-protected-resource")]
    public async Task There_is_no_metadata(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Get, path, NewSub())).StatusCode);
    }

    [Fact]
    public async Task There_is_no_MCP_server_and_Settings_has_no_connections()
    {
        var sub = await SetUpUser(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await McpSend(factory, TestTokens.Create(sub: sub), "tools/list")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Post, "/api/oauth/register", sub, new JsonObject())).StatusCode);
        Assert.Null((await Json(await Send(factory, HttpMethod.Get, "/api/settings", sub)))["connections"]);
    }
}
