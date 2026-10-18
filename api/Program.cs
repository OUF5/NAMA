using Api.Features.BankAccounts;
using Api.Features.Categories;
using Api.Features.Entries;
using Api.Features.FiscalYears;
using Api.Features.Income;
using Api.Features.Indicator;
using Api.Features.Overview;
using Api.Features.Plans;
using Api.Features.Profile;
using Api.Features.Settings;
using Api.Features.Setup;
using Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Cloud Run sends traffic to the port in the PORT variable (PRD 7.8).
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddProblemDetails();
builder.Services.AddFirebaseAuth();
// PRD 7.2: built-in Minimal API validation; errors come back as ProblemDetails.
builder.Services.AddValidation();
// R-CAL-10: tests replace it to fake "today".
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFirestore();

var app = builder.Build();

// Without the project id no token can be checked (PRD 7.6).
if (string.IsNullOrWhiteSpace(app.Configuration["FIREBASE_PROJECT_ID"]))
{
    throw new InvalidOperationException("FIREBASE_PROJECT_ID is not set.");
}

// PRD 7.5: empty error responses (401, 403, 404) get a ProblemDetails body.
app.UseStatusCodePages();

// NFR-03: API responses are never cached.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
    }
    await next();
});

// Added explicitly after the no-store middleware, so 401 and 403 responses get the header too.
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

// Liveness and version (git commit), no auth (PRD 7.5).
api.MapGet("/health", (IConfiguration config) =>
    new { status = "ok", version = config["APP_VERSION"] ?? "dev" })
    .AllowAnonymous();

api.MapMe();
api.MapProfile();
api.MapSettings();
api.MapSetup();
api.MapFiscalYears();
api.MapCategories();
api.MapPlans();
api.MapBankAccounts();
api.MapIncome();
api.MapEntries();
api.MapIndicator();
api.MapOverview();

app.Run();
