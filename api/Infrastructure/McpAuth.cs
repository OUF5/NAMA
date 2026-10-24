using System.Security.Claims;
using System.Text.Encodings.Web;
using Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure;

// MCP-03: /api/mcp takes only NAMA's own access tokens (this scheme), never a Firebase ID token, and
// the rest of the API never takes these (they are not JWTs). The connection's uid becomes "sub" and
// its email "email", so AUTH-02's check (AllowedUserHandler) runs on every call as it does for the app.
public static class McpAuth
{
    public const string Scheme = "NamaMcp";
    public const string Policy = "Mcp";
    public const string ConnectionClaim = "connection";

    // MCP-06: the site's origin, for example http://localhost:4200; null while the connector is off.
    public static string? Origin(IConfiguration config) =>
        config["PUBLIC_ORIGIN"]?.Trim().TrimEnd('/') is { Length: > 0 } origin ? origin : null;

    // RFC 8707: NAMA's MCP address, the resource its tokens are for.
    public static string Resource(string origin) => $"{origin}/api/mcp";

    public static string ResourceMetadataUrl(string origin) => $"{origin}/.well-known/oauth-protected-resource/api/mcp";

    public static IServiceCollection AddMcpAuth(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, McpTokenHandler>(Scheme, null);
        services.AddOptions<AuthorizationOptions>().Configure(options =>
            options.AddPolicy(Policy, new AuthorizationPolicyBuilder(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .RequireClaim("scope", OAuth.ReadScope)
                .AddRequirements(new AllowedUserRequirement())
                .Build()));
        return services;
    }
}

public sealed class McpTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConnectionStore connections,
    TimeProvider time,
    IConfiguration config)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }
        var token = header[BearerPrefix.Length..].Trim();
        var connection = token.Length == 0 ? null : await connections.FindByAccessTokenAsync(OAuth.Hash(token));
        if (connection is null || OAuth.IsExpired(connection.AccessExpiresAt, time.GetUtcNow()))
        {
            return AuthenticateResult.Fail("The access token is unknown, expired or disconnected.");
        }
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", connection.Uid),
                new Claim("email", connection.Email),
                // The email was verified when the person allowed the app (Connect needs a verified account).
                new Claim("email_verified", "true"),
                new Claim("scope", connection.Scope),
                new Claim(McpAuth.ConnectionClaim, connection.Id),
            ],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    // MCP-02 (RFC 9728 section 5.1): a 401 says where NAMA's sign-in is described and which scope to ask for.
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var origin = McpAuth.Origin(config)!;
        var result = await HandleAuthenticateOnceSafeAsync();
        var error = result.Failure is null ? "" : ", error=\"invalid_token\"";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate =
            $"Bearer resource_metadata=\"{McpAuth.ResourceMetadataUrl(origin)}\", scope=\"{OAuth.ReadScope}\"{error}";
    }
}
