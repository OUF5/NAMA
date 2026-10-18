using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Tests;

// Calls the API as one signed-in user, and sets up users straight in the in-memory store.
public static class ApiCalls
{
    // The email decides the role (AUTH-02): the default is an admin in ALLOWED_EMAILS.
    public static async Task<HttpResponseMessage> Send(ApiFactory factory, HttpMethod method, string url, string sub, JsonNode? body = null,
        string email = ApiFactory.AllowedEmail)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(email: email, sub: sub));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await factory.CreateClient().SendAsync(request);
    }

    public static async Task<JsonNode> Json(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonNode>())!;

    // A different user per test, so tests sharing the in-memory store don't meet.
    public static string NewSub() => "user-" + Guid.NewGuid().ToString("N");

    // FY 2026 of the section 3 example (payday 27, Jan to Dec 2026), bank accounts SNB and
    // Al Rajhi, categories Food and Transport, and the given income items.
    public static async Task<string> SetUpUser(ApiFactory factory, IReadOnlyList<IncomeItem>? incomes = null, FiscalYear? fiscalYear = null,
        string? sub = null)
    {
        sub ??= NewSub();
        var profile = new UserProfile("Owner", ApiFactory.AllowedEmail, "Home budget", "SAR", "Asia/Riyadh", 1, 27, true, null);
        await factory.Store.CreateSetupAsync(sub, new SetupData(
            profile,
            fiscalYear ?? FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 10, 17), 1, 27),
            incomes ?? [],
            [new BankAccount("bank", "SNB"), new BankAccount("rajhi", "Al Rajhi")],
            [new Category("food", "Food", 0), new Category("transport", "Transport", 1)]));
        return sub;
    }

    // AUTH-06: an admin (any sub with an ALLOWED_EMAILS email) creates a link; returns its token.
    public static async Task<string> CreateInvite(ApiFactory factory, string? adminSub = null)
    {
        var response = await Send(factory, HttpMethod.Post, "/api/invites", adminSub ?? NewSub());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response))["token"]!.GetValue<string>();
    }

    public static async Task<HttpResponseMessage> Accept(ApiFactory factory, string? token, string sub, string email, bool emailVerified = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/invites/accept")
        {
            Content = JsonContent.Create(new JsonObject { ["token"] = token }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(email: email, emailVerified: emailVerified, sub: sub));
        return await factory.CreateClient().SendAsync(request);
    }

    // A new person joins with a fresh link. Returns their member id.
    public static async Task<string> Join(ApiFactory factory, string sub, string email)
    {
        var response = await Accept(factory, await CreateInvite(factory), sub, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return factory.Access.Members.Single(member => member.Uid == sub).Id;
    }
}
