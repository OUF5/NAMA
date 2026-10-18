using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Api.Domain;

// AUTH-06: an invite link carries a random secret, works once, and expires 7 days after it was created.
public static class Invites
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    // 32 random bytes, base64url without padding (43 characters), so it fits in a URL as it is.
    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    // Only this hash is stored, as the invite's id (owner's choice): a stored invite cannot be
    // turned back into a working link. Lowercase hex, 64 characters.
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static DateTimeOffset ExpiresAt(DateTimeOffset createdAt) => createdAt + Lifetime;

    public static bool IsExpired(DateTimeOffset expiresAt, DateTimeOffset now) => now >= expiresAt;
}
