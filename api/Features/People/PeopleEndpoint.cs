using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.People;

// AUTH-06, AUTH-07, STG-07: invite links and Settings → People. Admins create and revoke links and
// remove people. Accepting a link is the one call open to a signed-in account that is not allowed yet.
public static class PeopleEndpoint
{
    // Dates are in the admin's profile time zone (YYYY-MM-DD). A member's uid is never sent.
    public record MemberDto(string Id, string Email, DateOnly JoinedOn);

    public record InviteDto(string Id, DateOnly CreatedOn, DateOnly ExpiresOn, bool Expired);

    // Also part of GET /api/settings for admins (NFR-01).
    public record PeopleDto(IReadOnlyList<string> Admins, IReadOnlyList<MemberDto> Members, IReadOnlyList<InviteDto> Invites);

    // The token is sent only here: just its hash is stored, so the link cannot be shown again.
    public record CreatedInviteResponse(string Token, DateOnly ExpiresOn, PeopleDto People);

    public record PeopleResponse(PeopleDto People);

    public static IEndpointRouteBuilder MapPeople(this IEndpointRouteBuilder api)
    {
        api.MapPost("/invites", async (ClaimsPrincipal user, IAccessStore access, IUserStore store, TimeProvider time, EmailAllowlist admins) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var now = time.GetUtcNow();
            var token = Invites.NewToken();
            var invite = new Invite(Invites.Hash(token), uid, user.FindFirstValue("email")!, now, Invites.ExpiresAt(now));
            await access.CreateInviteAsync(invite);
            var timezone = await TimeZoneAsync(uid, store);
            return Results.Created($"/api/invites/{invite.Id}", new CreatedInviteResponse(
                token, FiscalCalendar.DateIn(invite.ExpiresAt, timezone), await LoadAsync(access, admins, time, timezone)));
        }).RequireAuthorization(FirebaseAuth.AdminPolicy);

        // Revoke an unused link (expired ones too).
        api.MapDelete("/invites/{id}", async (string id, ClaimsPrincipal user, IAccessStore access, IUserStore store, TimeProvider time,
            EmailAllowlist admins) =>
            await access.DeleteInviteAsync(id)
                ? Results.Ok(new PeopleResponse(await LoadAsync(access, admins, time, await TimeZoneAsync(user.FindFirstValue("sub")!, store))))
                : Results.NotFound())
            .RequireAuthorization(FirebaseAuth.AdminPolicy);

        // Access ends on the person's next request. Their users/{uid} data is kept, and comes back if
        // the same Google account is invited again (AUTH-07).
        api.MapDelete("/people/{memberId}", async (string memberId, ClaimsPrincipal user, IAccessStore access, MemberCache members,
            IUserStore store, TimeProvider time, EmailAllowlist admins) =>
        {
            if (await access.DeleteMemberAsync(memberId) is not { } removed)
            {
                return Results.NotFound();
            }
            await members.RemoveAsync(removed.Uid);
            return Results.Ok(new PeopleResponse(await LoadAsync(access, admins, time, await TimeZoneAsync(user.FindFirstValue("sub")!, store))));
        }).RequireAuthorization(FirebaseAuth.AdminPolicy);

        // The caller is signed in but usually not allowed yet, so this route skips the fallback policy.
        // Someone already allowed (an admin or a member) leaves the link unused. Answers with /api/me,
        // so the web app needs no second call.
        api.MapPost("/invites/accept", async (AcceptRequest request, ClaimsPrincipal user, IAccessStore access, MemberCache members,
            IUserStore store, TimeProvider time, EmailAllowlist admins) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var isAdmin = admins.IsAdmin(user);
            if (!isAdmin && !await members.ContainsAsync(uid))
            {
                var (result, _) = await access.AcceptInviteAsync(Invites.Hash(request.Token!), uid, user.FindFirstValue("email")!, time.GetUtcNow());
                if (result == AcceptResult.NotFound)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: "This invite link was already used or revoked.");
                }
                if (result == AcceptResult.Expired)
                {
                    return Results.Problem(statusCode: StatusCodes.Status410Gone, detail: "This invite link has expired.");
                }
                await members.AddAsync(uid);
            }
            return Results.Ok(await MeEndpoint.LoadAsync(uid, isAdmin, store, time));
        }).RequireAuthorization(FirebaseAuth.SignedInPolicy);

        return api;
    }

    // Members by the date they joined, links newest first.
    public static async Task<PeopleDto> LoadAsync(IAccessStore access, EmailAllowlist admins, TimeProvider time, string timezone)
    {
        var now = time.GetUtcNow();
        var members = await access.GetMembersAsync();
        var invites = await access.GetInvitesAsync();
        return new PeopleDto(
            admins.Emails,
            members.OrderBy(member => member.JoinedAt)
                .Select(member => new MemberDto(member.Id, member.Email, FiscalCalendar.DateIn(member.JoinedAt, timezone)))
                .ToList(),
            invites.OrderByDescending(invite => invite.CreatedAt)
                .Select(invite => new InviteDto(invite.Id, FiscalCalendar.DateIn(invite.CreatedAt, timezone),
                    FiscalCalendar.DateIn(invite.ExpiresAt, timezone), Invites.IsExpired(invite.ExpiresAt, now)))
                .ToList());
    }

    // An admin may create a link before finishing setup, when there is no profile time zone yet.
    private static async Task<string> TimeZoneAsync(string uid, IUserStore store) =>
        (await store.GetProfileAsync(uid))?.Timezone ?? "UTC";
}

public sealed class AcceptRequest
{
    [Required]
    public string? Token { get; init; }
}
