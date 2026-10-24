using System.Security.Claims;
using System.Text.Json.Serialization;
using Api.Domain;
using Api.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;

namespace Api.Features.Connect;

// MCP-01, MCP-02, MCP-03: NAMA is its own OAuth 2.1 authorization server for the Claude connector.
// Metadata and registration are open (RFC 9728, RFC 8414, RFC 7591); Connect's two calls need an
// allowed user (the app's Firebase sign-in, AUTH-02); the token endpoint is open, as OAuth needs, and
// trusts only a code or refresh token it issued. Names in the OAuth bodies are OAuth's (snake_case).
public static class OAuthEndpoint
{
    public record ResourceMetadata(
        [property: JsonPropertyName("resource")] string Resource,
        [property: JsonPropertyName("authorization_servers")] IReadOnlyList<string> AuthorizationServers,
        [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
        [property: JsonPropertyName("bearer_methods_supported")] IReadOnlyList<string> BearerMethodsSupported,
        [property: JsonPropertyName("resource_name")] string ResourceName);

    public record AuthorizationServerMetadata(
        [property: JsonPropertyName("issuer")] string Issuer,
        [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
        [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
        [property: JsonPropertyName("registration_endpoint")] string RegistrationEndpoint,
        [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
        [property: JsonPropertyName("response_types_supported")] IReadOnlyList<string> ResponseTypesSupported,
        [property: JsonPropertyName("grant_types_supported")] IReadOnlyList<string> GrantTypesSupported,
        [property: JsonPropertyName("token_endpoint_auth_methods_supported")] IReadOnlyList<string> TokenEndpointAuthMethodsSupported,
        [property: JsonPropertyName("code_challenge_methods_supported")] IReadOnlyList<string> CodeChallengeMethodsSupported);

    public record RegisterRequest(
        [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string?>? RedirectUris,
        [property: JsonPropertyName("client_name")] string? ClientName);

    // Every client is public (MCP-02): whatever auth method was asked for, the answer says "none".
    public record RegisterResponse(
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("client_id_issued_at")] long ClientIdIssuedAt,
        [property: JsonPropertyName("client_name")] string ClientName,
        [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string> RedirectUris,
        [property: JsonPropertyName("grant_types")] IReadOnlyList<string> GrantTypes,
        [property: JsonPropertyName("response_types")] IReadOnlyList<string> ResponseTypes,
        [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod);

    public record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] long ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("scope")] string Scope);

    public record OAuthError(
        [property: JsonPropertyName("error")] string Error,
        [property: JsonPropertyName("error_description")] string ErrorDescription);

    // The authorization request as Connect read it from its address (camelCase from the web app).
    public record AuthorizeRequest(
        string? ResponseType,
        string? ClientId,
        string? RedirectUri,
        string? CodeChallenge,
        string? CodeChallengeMethod,
        string? State,
        string? Scope,
        string? Resource,
        bool Allow = false);

    // What Connect shows: the app's name and where it sends you back (MCP-01).
    public record CheckResponse(string ClientName, string RedirectHost, string Scope);

    public record AuthorizeResponse(string RedirectTo);

    private static readonly string[] Scopes = [OAuth.ReadScope];

    // Outside /api, at the origin's root, where clients look for them (MCP-02).
    public static IEndpointRouteBuilder MapOAuthMetadata(this IEndpointRouteBuilder app, string origin)
    {
        var resource = new ResourceMetadata(McpAuth.Resource(origin), [origin], Scopes, ["header"], "NAMA");
        app.MapGet("/.well-known/oauth-protected-resource", () => resource).AllowAnonymous();
        app.MapGet("/.well-known/oauth-protected-resource/api/mcp", () => resource).AllowAnonymous();
        app.MapGet("/.well-known/oauth-authorization-server", () => new AuthorizationServerMetadata(
            origin,
            $"{origin}/connect",
            $"{origin}/api/oauth/token",
            $"{origin}/api/oauth/register",
            Scopes,
            ["code"],
            ["authorization_code", "refresh_token"],
            ["none"],
            ["S256"])).AllowAnonymous();
        return app;
    }

    public static IEndpointRouteBuilder MapOAuth(this IEndpointRouteBuilder api, string origin)
    {
        // RFC 7591. Nothing is stored: the client id carries the name and redirect URIs (OAuth.ClientId).
        api.MapPost("/oauth/register", (RegisterRequest request, TimeProvider time) =>
        {
            if (OAuth.NewClient(request.ClientName, request.RedirectUris) is not { } client)
            {
                return Results.BadRequest(new OAuthError("invalid_redirect_uri",
                    "NAMA accepts only Claude's callbacks: http://localhost:PORT/callback or claude.ai's."));
            }
            return Results.Created("/api/oauth/register", new RegisterResponse(
                OAuth.ClientId(client), time.GetUtcNow().ToUnixTimeSeconds(), client.Name, client.RedirectUris,
                ["authorization_code", "refresh_token"], ["code"], "none"));
        }).AllowAnonymous();

        // MCP-01: Connect checks the request before it shows Allow. Every error is shown on the page and
        // sends you nowhere, because a request that fails a check cannot be trusted with a redirect.
        api.MapPost("/oauth/check", (AuthorizeRequest request) =>
            Check(request, origin) is { } error
                ? Invalid(error)
                : Results.Ok(new CheckResponse(OAuth.ReadClientId(request.ClientId)!.Name, OAuth.RedirectHost(request.RedirectUri!), OAuth.ReadScope)));

        // Allow: a code for this person, bound to the app, its redirect URI and the PKCE challenge. Cancel:
        // OAuth's access_denied. Either way the app gets back the state it sent.
        api.MapPost("/oauth/authorize", async (AuthorizeRequest request, ClaimsPrincipal user, IConnectionStore connections, TimeProvider time) =>
        {
            if (Check(request, origin) is { } error)
            {
                return Invalid(error);
            }
            var answer = new Dictionary<string, string?>();
            if (request.Allow)
            {
                var code = OAuth.NewToken();
                await connections.CreateCodeAsync(new AuthorizationCode(
                    OAuth.Hash(code),
                    user.FindFirstValue("sub")!,
                    user.FindFirstValue("email")!,
                    request.ClientId!,
                    OAuth.ReadClientId(request.ClientId)!.Name,
                    request.RedirectUri!,
                    request.CodeChallenge!,
                    OAuth.ReadScope,
                    time.GetUtcNow() + OAuth.CodeLifetime));
                answer["code"] = code;
            }
            else
            {
                answer["error"] = "access_denied";
            }
            if (!string.IsNullOrEmpty(request.State))
            {
                answer["state"] = request.State;
            }
            return Results.Ok(new AuthorizeResponse(QueryHelpers.AddQueryString(request.RedirectUri!, answer)));
        });

        // MCP-03. Form-encoded (RFC 6749 section 4.1.3), read by hand so no antiforgery token is needed.
        api.MapPost("/oauth/token", async (HttpRequest request, IConnectionStore connections, TimeProvider time) =>
        {
            if (!request.HasFormContentType)
            {
                return TokenError("invalid_request", "Send the request form-encoded.");
            }
            var form = await request.ReadFormAsync();
            return form["grant_type"].ToString() switch
            {
                "authorization_code" => await ExchangeCodeAsync(form, origin, connections, time),
                "refresh_token" => await RefreshAsync(form, connections, time),
                _ => TokenError("unsupported_grant_type", "Use authorization_code or refresh_token."),
            };
        }).AllowAnonymous();

        return api;
    }

    // Null when the request is one NAMA can answer. The client and its redirect URI come first: without
    // them nothing else can be told to the app.
    private static string? Check(AuthorizeRequest request, string origin)
    {
        if (OAuth.ReadClientId(request.ClientId) is not { } client)
        {
            return "This app is not registered with NAMA. Connect it again from the app.";
        }
        if (request.RedirectUri is null || !client.RedirectUris.Contains(request.RedirectUri, StringComparer.Ordinal))
        {
            return "This app asked to go back to an address it did not register. Connect it again from the app.";
        }
        if (request.ResponseType != "code")
        {
            return "This app asked for a kind of sign-in NAMA does not offer.";
        }
        if (request.CodeChallengeMethod != "S256" || !OAuth.IsChallenge(request.CodeChallenge))
        {
            return "This app did not send the security check (PKCE) NAMA needs.";
        }
        if (!OAuth.IsResource(request.Resource, McpAuth.Resource(origin)))
        {
            return "This app asked for access to something other than NAMA.";
        }
        return null;
    }

    private static IResult Invalid(string message) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "This connection request is not valid.", detail: message);

    private static async Task<IResult> ExchangeCodeAsync(IFormCollection form, string origin, IConnectionStore connections, TimeProvider time)
    {
        var code = form["code"].ToString();
        if (code.Length == 0 || await connections.TakeCodeAsync(OAuth.Hash(code)) is not { } stored)
        {
            return TokenError("invalid_grant", "The code is unknown or was already used.");
        }
        var now = time.GetUtcNow();
        if (OAuth.IsExpired(stored.ExpiresAt, now)
            || form["client_id"].ToString() != stored.ClientId
            || form["redirect_uri"].ToString() != stored.RedirectUri
            || !OAuth.VerifyPkce(form["code_verifier"].ToString(), stored.CodeChallenge)
            || !OAuth.IsResource(form["resource"].ToString(), McpAuth.Resource(origin)))
        {
            return TokenError("invalid_grant", "The code is expired, or does not match this app, its redirect URI or its PKCE verifier.");
        }
        var (access, refresh) = (OAuth.NewToken(), OAuth.NewToken());
        await connections.CreateConnectionAsync(new Connection(
            DocumentIds.New(),
            stored.Uid,
            stored.Email,
            stored.ClientId,
            stored.ClientName,
            stored.Scope,
            OAuth.Hash(access),
            now + OAuth.AccessLifetime,
            OAuth.Hash(refresh),
            now + OAuth.RefreshLifetime,
            now));
        return Tokens(access, refresh, stored.Scope);
    }

    // MCP-03: each refresh token works once and is replaced, with a new access token, in one transaction.
    private static async Task<IResult> RefreshAsync(IFormCollection form, IConnectionStore connections, TimeProvider time)
    {
        var token = form["refresh_token"].ToString();
        var clientId = form["client_id"].ToString();
        var now = time.GetUtcNow();
        var (access, refresh) = (OAuth.NewToken(), OAuth.NewToken());
        var refreshed = token.Length == 0 ? null : await connections.RefreshAsync(OAuth.Hash(token), connection =>
            connection.ClientId != clientId || OAuth.IsExpired(connection.RefreshExpiresAt, now)
                ? null
                : connection with
                {
                    AccessTokenHash = OAuth.Hash(access),
                    AccessExpiresAt = now + OAuth.AccessLifetime,
                    RefreshTokenHash = OAuth.Hash(refresh),
                    RefreshExpiresAt = now + OAuth.RefreshLifetime,
                    LastUsedAt = now,
                });
        return refreshed is null
            ? TokenError("invalid_grant", "The refresh token is unknown, expired, already used or disconnected.")
            : Tokens(access, refresh, refreshed.Scope);
    }

    private static IResult Tokens(string access, string refresh, string scope) =>
        Results.Ok(new TokenResponse(access, "Bearer", (long)OAuth.AccessLifetime.TotalSeconds, refresh, scope));

    private static IResult TokenError(string error, string description) => Results.BadRequest(new OAuthError(error, description));
}
