using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Api.Domain;

// MCP-02, MCP-03: the Claude connector's OAuth 2.1 rules (MCP authorization, 2025-11-25).
public static partial class OAuth
{
    // MCP-03: read-only for Step 1.
    public const string ReadScope = "nama.read";

    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    public const int ClientNameMaxLength = 100;
    private const int MaxRedirectUris = 10;
    private const string ClientIdPrefix = "nama_";
    private const string DefaultClientName = "An MCP app";

    // claude.ai's callback (Step 3); it may move to claude.com, so both are accepted.
    private static readonly string[] ClaudeWebCallbacks =
    [
        "https://claude.ai/api/mcp/auth_callback",
        "https://claude.com/api/mcp/auth_callback",
    ];

    // Tokens and codes have the invite link's shape (32 random bytes, base64url), and only their hash is
    // stored, the same way.
    public static string NewToken() => Invites.NewToken();

    public static string Hash(string token) => Invites.Hash(token);

    public static bool IsExpired(DateTimeOffset expiresAt, DateTimeOffset now) => now >= expiresAt;

    // Claude's callbacks only (MCP-02): Claude Code's http://localhost:PORT/callback (or 127.0.0.1), and
    // claude.ai's. Compared exactly: no other path, no query, no fragment.
    public static bool IsClaudeCallback(string? uri)
    {
        if (uri is null)
        {
            return false;
        }
        if (ClaudeWebCallbacks.Contains(uri, StringComparer.Ordinal))
        {
            return true;
        }
        var match = LoopbackCallback().Match(uri);
        return match.Success && int.TryParse(match.Groups["port"].Value, out var port) && port is >= 1 and <= 65535;
    }

    // What the Connect page shows as "sends you back to": "localhost:33418" or "claude.ai".
    public static string RedirectHost(string uri) => new Uri(uri).Authority;

    // RFC 7636 S256. A verifier is 43 to 128 unreserved characters; the challenge is the base64url
    // SHA-256 of it (43 characters).
    public static bool IsChallenge(string? challenge) => challenge is not null && Base64UrlSha256().IsMatch(challenge);

    public static bool VerifyPkce(string? verifier, string challenge)
    {
        if (verifier is null || !Verifier().IsMatch(verifier))
        {
            return false;
        }
        var computed = Encoding.ASCII.GetBytes(Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        return CryptographicOperations.FixedTimeEquals(computed, Encoding.ASCII.GetBytes(challenge));
    }

    // RFC 8707: when a client names the resource, it must be NAMA's MCP address. Scheme and host ignore
    // case, and a trailing slash is ignored. Not sent is fine (the token only works there anyway).
    public static bool IsResource(string? sent, string resource) =>
        string.IsNullOrEmpty(sent) || (Canonical(sent) is { } canonical && canonical == Canonical(resource));

    // Uri lowercases the scheme and host; the path keeps its case. No query or fragment.
    private static string? Canonical(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Query.Length == 0 && parsed.Fragment.Length == 0
            ? parsed.GetLeftPart(UriPartial.Authority) + parsed.AbsolutePath.TrimEnd('/')
            : null;

    // A registered app (RFC 7591). Nothing is stored (owner's choice, so open registration cannot fill
    // Firestore): the client id carries the name and the redirect URIs, and is read again on every use.
    // A made-up client id gains nothing, because every redirect URI must still be one of Claude's.
    public sealed record Client(string Name, IReadOnlyList<string> RedirectUris);

    // Null when a redirect URI is not one of Claude's, or there are none or too many.
    public static Client? NewClient(string? name, IReadOnlyList<string?>? redirectUris)
    {
        if (redirectUris is null || redirectUris.Count is 0 or > MaxRedirectUris || !redirectUris.All(IsClaudeCallback))
        {
            return null;
        }
        return new Client(ClientName(name), redirectUris.Distinct(StringComparer.Ordinal).ToList()!);
    }

    public static string ClientId(Client client) =>
        ClientIdPrefix + Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new ClientIdBody(client.Name, client.RedirectUris)));

    // Null when it is not a client id NAMA made, or no longer passes NewClient's checks.
    public static Client? ReadClientId(string? clientId)
    {
        if (clientId is null || !clientId.StartsWith(ClientIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }
        try
        {
            var body = JsonSerializer.Deserialize<ClientIdBody>(Base64Url.DecodeFromChars(clientId.AsSpan(ClientIdPrefix.Length)));
            return body is null ? null : NewClient(body.N, body.R);
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    // Trimmed, without control characters, at most 100 characters.
    private static string ClientName(string? name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return DefaultClientName;
        }
        return clean.Length <= ClientNameMaxLength ? clean : clean[..ClientNameMaxLength].TrimEnd();
    }

    // Short keys keep the client id short.
    private sealed record ClientIdBody(string? N, IReadOnlyList<string?>? R);

    [GeneratedRegex(@"^http://(localhost|127\.0\.0\.1):(?<port>[0-9]{1,5})/callback$")]
    private static partial Regex LoopbackCallback();

    [GeneratedRegex(@"^[A-Za-z0-9\-._~]{43,128}$")]
    private static partial Regex Verifier();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{43}$")]
    private static partial Regex Base64UrlSha256();
}
