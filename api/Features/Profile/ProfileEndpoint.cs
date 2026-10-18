using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Features.Profile;

// PRD 7.5 PUT /api/profile (STG-01): name, project name, currency and time zone.
public static class ProfileEndpoint
{
    public static IEndpointRouteBuilder MapProfile(this IEndpointRouteBuilder api)
    {
        // Answers with the new /api/me: the time zone moves "today" and the current month (R-CAL-10),
        // and the names and currency are shown everywhere (UX-02, R-MON-03).
        api.MapPut("/profile", async (ProfileRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (await store.GetProfileAsync(uid) is null)
            {
                return Results.NotFound();
            }
            await store.UpdateProfileAsync(uid, Names.Normalize(request.DisplayName), Names.Normalize(request.ProjectName),
                request.Currency!, request.Timezone!);
            return Results.Ok(await MeEndpoint.LoadAsync(uid, store, time));
        });
        return api;
    }
}

// The same checks as the wizard's step 1 and SET-04 (SetupRequest).
public sealed class ProfileRequest : IValidatableObject
{
    public string? DisplayName { get; init; }
    public string? ProjectName { get; init; }
    public string? Currency { get; init; }

    // An IANA id, for example "Asia/Riyadh".
    public string? Timezone { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Names.IsValid(DisplayName))
        {
            yield return new ValidationResult("Your name is 1 to 40 characters.", [nameof(DisplayName)]);
        }
        if (!Names.IsValid(ProjectName))
        {
            yield return new ValidationResult("Project name is 1 to 40 characters.", [nameof(ProjectName)]);
        }
        // R-MON-03: formatting only.
        if (Currency is null || !Money.Currencies.Contains(Currency))
        {
            yield return new ValidationResult($"Currency is one of {string.Join(", ", Money.Currencies)}.", [nameof(Currency)]);
        }
        if (string.IsNullOrEmpty(Timezone) || !TimeZoneInfo.TryFindSystemTimeZoneById(Timezone, out _))
        {
            yield return new ValidationResult("Time zone is unknown.", [nameof(Timezone)]);
        }
    }
}
