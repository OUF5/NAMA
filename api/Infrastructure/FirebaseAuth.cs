using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Api.Infrastructure;

// PRD 7.6: the API checks Firebase ID tokens itself (AUTH-03), and only verified, allowed users get
// in (AUTH-02): an admin email from ALLOWED_EMAILS, or a person who joined with an invite link
// (AUTH-06). This is the fallback policy, so every endpoint needs it unless it opts out with
// AllowAnonymous or names another policy (NFR-03).
public static class FirebaseAuth
{
    // Settings → People and the invite links (AUTH-07).
    public const string AdminPolicy = "Admin";

    // A verified Google account that may not be allowed yet: only for accepting an invite (AUTH-06).
    public const string SignedInPolicy = "SignedIn";

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

        services.AddSingleton(provider => new EmailAllowlist(provider.GetRequiredService<IConfiguration>()["ALLOWED_EMAILS"]));
        services.AddSingleton<MemberCache>();
        services.AddSingleton<IAuthorizationHandler, AllowedUserHandler>();

        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>()
            .Configure<EmailAllowlist>((options, admins) =>
            {
                options.FallbackPolicy = SignedIn().AddRequirements(new AllowedUserRequirement()).Build();
                options.AddPolicy(AdminPolicy, SignedIn().RequireAssertion(context => admins.IsAdmin(context.User)).Build());
                options.AddPolicy(SignedInPolicy, SignedIn().Build());
            });

        return services;
    }

    // A valid token with a "sub" claim and a verified email.
    private static AuthorizationPolicyBuilder SignedIn() => new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim("sub")
        .RequireAssertion(context => IsVerified(context.User));

    private static bool IsVerified(ClaimsPrincipal user) =>
        bool.TryParse(user.FindFirst("email_verified")?.Value, out var verified) && verified;
}

public sealed class AllowedUserRequirement : IAuthorizationRequirement;

// AUTH-02: admins are checked from the token alone, so they never wait on Firestore and can never be
// locked out. Members are looked up in memory (MemberCache), not read on every request.
public sealed class AllowedUserHandler(EmailAllowlist admins, MemberCache members) : AuthorizationHandler<AllowedUserRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AllowedUserRequirement requirement)
    {
        if (admins.IsAdmin(context.User)
            || (context.User.FindFirstValue("sub") is { } uid && await members.ContainsAsync(uid)))
        {
            context.Succeed(requirement);
        }
    }
}
