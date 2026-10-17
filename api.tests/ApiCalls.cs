using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Tests;

// Calls the API as one signed-in user, and sets up users straight in the in-memory store.
public static class ApiCalls
{
    public static async Task<HttpResponseMessage> Send(ApiFactory factory, HttpMethod method, string url, string sub, JsonNode? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(sub: sub));
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

    // FY 2026 of the section 3 example (payday 27, Jan to Dec 2026), categories Food and Transport,
    // and the given income items.
    public static async Task<string> SetUpUser(ApiFactory factory, IReadOnlyList<IncomeItem>? incomes = null, FiscalYear? fiscalYear = null)
    {
        var sub = NewSub();
        var profile = new UserProfile("Owner", ApiFactory.AllowedEmail, "Home budget", "SAR", "Asia/Riyadh", 1, 27, true, null);
        await factory.Store.CreateSetupAsync(sub, new SetupData(
            profile,
            fiscalYear ?? FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 10, 17), 1, 27),
            incomes ?? [],
            [new BankAccount("bank", "SNB")],
            [new Category("food", "Food", 0), new Category("transport", "Transport", 1)]));
        return sub;
    }
}
