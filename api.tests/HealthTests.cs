using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;

namespace Api.Tests;

// No token is sent: /api/health is the only endpoint without auth (NFR-03).
public class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record HealthResponse(string Status, string Version);

    [Fact]
    public async Task Health_returns_200_with_version_and_no_store()
    {
        var client = factory
            .WithWebHostBuilder(b => b.UseSetting("APP_VERSION", "test-sha"))
            .CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body!.Status);
        Assert.Equal("test-sha", body.Version);
    }
}
