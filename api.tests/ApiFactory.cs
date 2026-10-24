using Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Api.Tests;

// The API with test settings. Tokens are checked against TestTokens.SigningKey,
// so tests never fetch Google's public keys. Data lives in memory and "today" is TestTime.
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string ProjectId = "nama-test";
    public const string AllowedEmail = "owner@example.com";
    public const string SecondAllowedEmail = "second@example.com";

    public InMemoryUserStore Store { get; } = new();
    public InMemoryAccessStore Access { get; } = new();
    public TestTime Time { get; } = new();
    public InMemoryConnectionStore Connections { get; }

    // The test server's own address, so the connector's URLs point at it (MCP-06).
    public const string Origin = "http://localhost";

    public ApiFactory()
    {
        Connections = new InMemoryConnectionStore(Time);
    }

    // MCP-06: null turns the Claude connector off.
    protected virtual string? PublicOrigin => Origin;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("FIREBASE_PROJECT_ID", ProjectId);
        builder.UseSetting("ALLOWED_EMAILS", $" {AllowedEmail} , {SecondAllowedEmail},");
        // Also replaces appsettings.Development.json's local origin.
        builder.UseSetting("PUBLIC_ORIGIN", PublicOrigin ?? "");
        builder.ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = new OpenIdConnectConfiguration();
                options.Configuration.SigningKeys.Add(TestTokens.SigningKey);
            });
            services.AddSingleton<IUserStore>(Store);
            services.AddSingleton<IAccessStore>(Access);
            services.AddSingleton<IConnectionStore>(Connections);
            services.AddSingleton<TimeProvider>(Time);
        });
    }
}
