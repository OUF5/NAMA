using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Api.Infrastructure;

// PRD 7.6: the API checks Firebase ID tokens itself (AUTH-03), and only verified,
// allowlisted emails get in (AUTH-02). This is the fallback policy, so every endpoint
// needs it unless it opts out with AllowAnonymous (NFR-03).
public static class FirebaseAuth
{
    public static IServiceCollection AddFirebaseAuth(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Settings are read when the options are first used, so every configuration source applies.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, config) =>
            {
                var projectId = config["FIREBASE_PROJECT_ID"];
                options.Authority = $"https://securetoken.google.com/{projectId}";
                // Keep "sub" and "email" under their own names.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidIssuer = options.Authority;
                options.TokenValidationParameters.ValidAudience = projectId;
                options.TokenValidationParameters.ValidateLifetime = true;
            });

        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                var allowlist = new EmailAllowlist(config["ALLOWED_EMAILS"]);
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .RequireClaim("sub")
                    .RequireAssertion(context => IsVerifiedAndAllowed(context.User, allowlist))
                    .Build();
            });

        return services;
    }

    private static bool IsVerifiedAndAllowed(ClaimsPrincipal user, EmailAllowlist allowlist) =>
        bool.TryParse(user.FindFirst("email_verified")?.Value, out var verified)
        && verified
        && allowlist.IsAllowed(user.FindFirst("email")?.Value);
}
