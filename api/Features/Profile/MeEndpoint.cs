namespace Api.Features.Profile;

// PRD 7.5 GET /api/me, called right after sign-in. Nobody can be set up before the setup
// wizard exists (M3), so the profile, current FY and current month are still empty.
public static class MeEndpoint
{
    public record MeResponse(bool SetupCompleted, object? Profile, string? CurrentFyId, string? CurrentPeriodKey);

    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder api)
    {
        api.MapGet("/me", () => new MeResponse(SetupCompleted: false, Profile: null, CurrentFyId: null, CurrentPeriodKey: null));
        return api;
    }
}
