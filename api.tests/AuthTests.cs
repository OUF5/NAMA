using System.Net;
using System.Net.Http.Headers;

namespace Api.Tests;

// AUTH-02, AUTH-03, NFR-03: every endpoint except /api/health needs a valid Firebase ID token
// and a verified, allowlisted email. Errors are ProblemDetails (PRD 7.5) and never cached.
public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpResponseMessage> GetMe(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await factory.CreateClient().SendAsync(request);
    }

    private static void AssertNoStoreProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Me_without_token_returns_401()
    {
        AssertNoStoreProblem(await GetMe(null), HttpStatusCode.Unauthorized);
    }

    public static TheoryData<string, string> InvalidTokens => new()
    {
        { "garbage", "not-a-jwt" },
        { "wrong audience", TestTokens.Create(audience: "other-project") },
        { "wrong issuer", TestTokens.Create(issuer: "https://securetoken.google.com/other-project") },
        { "expired", TestTokens.Create(expires: DateTime.UtcNow.AddHours(-1)) },
        { "signed by another key", TestTokens.Create(key: TestTokens.OtherKey) },
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task Me_with_invalid_token_returns_401(string reason, string token)
    {
        _ = reason;
        AssertNoStoreProblem(await GetMe(token), HttpStatusCode.Unauthorized);
    }

    public static TheoryData<string, string> ForbiddenTokens => new()
    {
        { "email not allowlisted", TestTokens.Create(email: "stranger@example.com") },
        { "email not verified", TestTokens.Create(emailVerified: false) },
        { "no email", TestTokens.Create(email: null) },
        { "no sub", TestTokens.Create(sub: null) },
    };

    [Theory]
    [MemberData(nameof(ForbiddenTokens))]
    public async Task Me_with_valid_token_but_not_allowed_returns_403(string reason, string token)
    {
        _ = reason;
        AssertNoStoreProblem(await GetMe(token), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(ApiFactory.AllowedEmail)]
    [InlineData("OWNER@Example.COM")]
    [InlineData(ApiFactory.SecondAllowedEmail)]
    public async Task Me_with_allowlisted_email_returns_200_not_set_up(string email)
    {
        var response = await GetMe(TestTokens.Create(email: email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            """{"setupCompleted":false,"profile":null,"currentFyId":null,"currentPeriodKey":null,"currentPeriod":null,"today":null,"firstDate":null,"lastDate":null}""",
            await response.Content.ReadAsStringAsync());
    }
}
