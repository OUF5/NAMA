using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Tests;

// Mints tokens shaped like Firebase ID tokens: RS256 with iss, aud, sub, email and email_verified.
public static class TestTokens
{
    public static readonly RsaSecurityKey SigningKey = NewKey("test-key");
    public static readonly RsaSecurityKey OtherKey = NewKey("other-key");

    public static string Create(
        string? email = ApiFactory.AllowedEmail,
        bool emailVerified = true,
        string? sub = "user-1",
        string issuer = "https://securetoken.google.com/" + ApiFactory.ProjectId,
        string audience = ApiFactory.ProjectId,
        DateTime? expires = null,
        SecurityKey? key = null)
    {
        var claims = new Dictionary<string, object> { ["email_verified"] = emailVerified };
        if (sub is not null) claims["sub"] = sub;
        if (email is not null) claims["email"] = email;

        var exp = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = exp.AddHours(-1),
            NotBefore = exp.AddHours(-1),
            Expires = exp,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private static RsaSecurityKey NewKey(string keyId) => new(RSA.Create(2048)) { KeyId = keyId };
}
