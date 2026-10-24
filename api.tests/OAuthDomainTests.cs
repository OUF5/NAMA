using System.Buffers.Text;
using System.Text;
using Api.Domain;

namespace Api.Tests;

// MCP-02, MCP-03: the connector's OAuth rules.
public class OAuthDomainTests
{
    [Theory]
    [InlineData("http://localhost:33418/callback", true)]
    [InlineData("http://127.0.0.1:8080/callback", true)]
    [InlineData("http://localhost:1/callback", true)]
    [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
    [InlineData("https://claude.com/api/mcp/auth_callback", true)]
    [InlineData("http://localhost/callback", false)]
    [InlineData("http://localhost:0/callback", false)]
    [InlineData("http://localhost:65536/callback", false)]
    [InlineData("http://localhost:3000/oauth/callback", false)]
    [InlineData("http://localhost:3000/callback?x=1", false)]
    [InlineData("http://localhost:3000/callback#x", false)]
    [InlineData("https://localhost:3000/callback", false)]
    [InlineData("http://localhost.evil.example:3000/callback", false)]
    [InlineData("http://192.168.1.5:3000/callback", false)]
    [InlineData("https://claude.ai/api/mcp/auth_callback/", false)]
    [InlineData("https://evil.example/api/mcp/auth_callback", false)]
    [InlineData("https://claude.ai.evil.example/api/mcp/auth_callback", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_Claudes_callbacks_are_accepted(string? uri, bool expected)
    {
        Assert.Equal(expected, OAuth.IsClaudeCallback(uri));
    }

    // RFC 7636 Appendix B.
    [Fact]
    public void Pkce_S256_matches_the_RFC_example()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        Assert.True(OAuth.IsChallenge(challenge));
        Assert.True(OAuth.VerifyPkce(verifier, challenge));
        Assert.False(OAuth.VerifyPkce(verifier[..^1] + "Y", challenge));
        Assert.False(OAuth.VerifyPkce(null, challenge));
    }

    [Theory]
    [InlineData(42, false)]
    [InlineData(43, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void A_verifier_is_43_to_128_characters(int length, bool expected)
    {
        var verifier = new string('a', length);
        var challenge = Base64Url.EncodeToString(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        Assert.Equal(expected, OAuth.VerifyPkce(verifier, challenge));
    }

    [Theory]
    [InlineData("plain-challenge")]
    [InlineData("")]
    [InlineData(null)]
    public void A_challenge_is_a_base64url_sha256(string? challenge)
    {
        Assert.False(OAuth.IsChallenge(challenge));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("http://localhost:4200/api/mcp", true)]
    [InlineData("http://localhost:4200/api/mcp/", true)]
    [InlineData("HTTP://LOCALHOST:4200/api/mcp", true)]
    [InlineData("http://localhost:4200/api/MCP", false)]
    [InlineData("http://localhost:4200", false)]
    [InlineData("http://localhost:4201/api/mcp", false)]
    [InlineData("http://localhost:4200/api/mcp?x=1", false)]
    [InlineData("http://localhost:4200/api/mcp#x", false)]
    [InlineData("not a url", false)]
    public void The_resource_is_NAMAs_MCP_address(string? sent, bool expected)
    {
        Assert.Equal(expected, OAuth.IsResource(sent, "http://localhost:4200/api/mcp"));
    }

    [Fact]
    public void A_client_id_carries_the_name_and_redirect_uris()
    {
        var client = OAuth.NewClient("  Claude Code (nama)  ", ["http://localhost:33418/callback", "http://localhost:33418/callback"])!;

        var read = OAuth.ReadClientId(OAuth.ClientId(client))!;

        Assert.Equal("Claude Code (nama)", read.Name);
        Assert.Equal(["http://localhost:33418/callback"], read.RedirectUris);
        Assert.StartsWith("nama_", OAuth.ClientId(client));
    }

    [Fact]
    public void A_made_up_client_id_with_another_callback_is_refused()
    {
        var forged = "nama_" + Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"N":"Claude","R":["https://evil.example/callback"]}"""));

        Assert.Null(OAuth.ReadClientId(forged));
        Assert.Null(OAuth.ReadClientId("nama_not-base64!"));
        Assert.Null(OAuth.ReadClientId("nama_" + Base64Url.EncodeToString(Encoding.UTF8.GetBytes("not json"))));
        Assert.Null(OAuth.ReadClientId("someone-else"));
        Assert.Null(OAuth.ReadClientId(null));
    }

    [Fact]
    public void Registration_needs_1_to_10_of_Claudes_callbacks()
    {
        var eleven = Enumerable.Range(1, 11).Select(port => (string?)$"http://localhost:{port}/callback").ToList();

        Assert.Null(OAuth.NewClient("Claude", []));
        Assert.Null(OAuth.NewClient("Claude", null));
        Assert.Null(OAuth.NewClient("Claude", ["http://localhost:3000/callback", "https://evil.example/callback"]));
        Assert.Null(OAuth.NewClient("Claude", eleven));
        Assert.NotNull(OAuth.NewClient("Claude", eleven[..10]));
    }

    [Fact]
    public void A_client_name_is_cleaned_and_at_most_100_characters()
    {
        Assert.Equal("An MCP app", OAuth.NewClient(null, ["http://localhost:3000/callback"])!.Name);
        Assert.Equal("An MCP app", OAuth.NewClient(" \n ", ["http://localhost:3000/callback"])!.Name);
        Assert.Equal("Claude Code", OAuth.NewClient("Claude\u0000 Code", ["http://localhost:3000/callback"])!.Name);
        Assert.Equal(100, OAuth.NewClient(new string('x', 150), ["http://localhost:3000/callback"])!.Name.Length);
    }

    [Theory]
    [InlineData("http://localhost:33418/callback", "localhost:33418")]
    [InlineData("https://claude.ai/api/mcp/auth_callback", "claude.ai")]
    public void Connect_names_where_the_app_sends_you(string uri, string expected)
    {
        Assert.Equal(expected, OAuth.RedirectHost(uri));
    }

    [Fact]
    public void Codes_last_5_minutes_access_1_hour_refresh_30_days()
    {
        var now = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

        Assert.Equal((TimeSpan.FromMinutes(5), TimeSpan.FromHours(1), TimeSpan.FromDays(30)),
            (OAuth.CodeLifetime, OAuth.AccessLifetime, OAuth.RefreshLifetime));
        Assert.False(OAuth.IsExpired(now.AddTicks(1), now));
        Assert.True(OAuth.IsExpired(now, now));
    }
}
