var builder = WebApplication.CreateBuilder(args);

// Cloud Run sends traffic to the port in the PORT variable (PRD 7.8).
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var app = builder.Build();

// NFR-03: API responses are never cached.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
    }
    await next();
});

var api = app.MapGroup("/api");

// Liveness and version (git commit), no auth (PRD 7.5).
api.MapGet("/health", (IConfiguration config) =>
    new { status = "ok", version = config["APP_VERSION"] ?? "dev" });

app.Run();
