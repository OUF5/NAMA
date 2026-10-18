using System.Net;
using Api.Domain;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// AUTH-02, AUTH-06: an admin creates a single-use link that expires after 7 days. Whoever opens it
// and signs in with Google is allowed from then on. Today is 17 Oct 2026, 12:00 in Riyadh.
public class InvitesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string NewEmail() => $"friend-{Guid.NewGuid():N}@example.com";

    private Task<HttpResponseMessage> GetMe(string sub, string email) => Send(factory, HttpMethod.Get, "/api/me", sub, email: email);

    [Fact]
    public async Task A_stranger_joins_with_a_link_and_lands_on_setup()
    {
        var admin = await SetUpUser(factory);
        var (sub, email) = (NewSub(), NewEmail());
        Assert.Equal(HttpStatusCode.Forbidden, (await GetMe(sub, email)).StatusCode);

        var created = await Send(factory, HttpMethod.Post, "/api/invites", admin);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await Json(created);
        var token = body["token"]!.GetValue<string>();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        // 24 Oct 2026, 12:00 in Riyadh, the admin's time zone.
        Assert.Equal("2026-10-24", body["expiresOn"]!.GetValue<string>());
        var listed = body["people"]!["invites"]!.AsArray().Single(invite => invite!["id"]!.GetValue<string>() == Invites.Hash(token))!;
        Assert.Equal(("2026-10-17", "2026-10-24", false),
            (listed["createdOn"]!.GetValue<string>(), listed["expiresOn"]!.GetValue<string>(), listed["expired"]!.GetValue<bool>()));
        // Only the hash is stored.
        Assert.DoesNotContain(factory.Access.Invites, invite => invite.Id == token);

        var accepted = await Accept(factory, token, sub, email);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var me = await Json(accepted);
        Assert.Equal((false, false), (me["setupCompleted"]!.GetValue<bool>(), me["isAdmin"]!.GetValue<bool>()));
        Assert.Equal(HttpStatusCode.OK, (await GetMe(sub, email)).StatusCode);
        var member = factory.Access.Members.Single(stored => stored.Uid == sub);
        Assert.Equal((email, TestTime.Default, ApiFactory.AllowedEmail), (member.Email, member.JoinedAt, member.InvitedByEmail));
        Assert.DoesNotContain(factory.Access.Invites, invite => invite.Id == Invites.Hash(token));
    }

    [Fact]
    public async Task A_link_works_once()
    {
        var token = await CreateInvite(factory);
        var (first, second) = ((Sub: NewSub(), Email: NewEmail()), (Sub: NewSub(), Email: NewEmail()));

        Assert.Equal(HttpStatusCode.OK, (await Accept(factory, token, first.Sub, first.Email)).StatusCode);
        var again = await Accept(factory, token, second.Sub, second.Email);

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetMe(second.Sub, second.Email)).StatusCode);
    }

    [Fact]
    public async Task Two_people_accepting_at_the_same_time_let_one_in()
    {
        var token = await CreateInvite(factory);

        var answers = await Task.WhenAll(Accept(factory, token, NewSub(), NewEmail()), Accept(factory, token, NewSub(), NewEmail()));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.NotFound], answers.Select(answer => answer.StatusCode).Order());
    }

    // Created at TestTime.Default, so it expires exactly 7 days later.
    [Theory]
    [InlineData(-1, HttpStatusCode.OK)]
    [InlineData(0, HttpStatusCode.Gone)]
    [InlineData(86_400, HttpStatusCode.Gone)]
    public async Task A_link_expires_7_days_after_it_was_created(int secondsAfterExpiry, HttpStatusCode expected)
    {
        var token = await CreateInvite(factory);
        var (sub, email) = (NewSub(), NewEmail());
        factory.Time.UtcNow = TestTime.Default.AddDays(7).AddSeconds(secondsAfterExpiry);
        try
        {
            var answer = await Accept(factory, token, sub, email);

            Assert.Equal(expected, answer.StatusCode);
            Assert.Equal(expected == HttpStatusCode.OK ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await GetMe(sub, email)).StatusCode);
        }
        finally
        {
            factory.Time.UtcNow = TestTime.Default;
        }
    }

    [Fact]
    public async Task A_revoked_link_is_refused()
    {
        var token = await CreateInvite(factory);
        var id = Invites.Hash(token);

        var revoked = await Send(factory, HttpMethod.Delete, $"/api/invites/{id}", NewSub());

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.DoesNotContain((await Json(revoked))["people"]!["invites"]!.AsArray(), invite => invite!["id"]!.GetValue<string>() == id);
        Assert.Equal(HttpStatusCode.NotFound, (await Accept(factory, token, NewSub(), NewEmail())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Delete, $"/api/invites/{id}", NewSub())).StatusCode);
    }

    // Opening a link while already allowed answers /api/me and leaves the link for the person it was meant for.
    [Fact]
    public async Task Admins_and_members_do_not_use_up_a_link()
    {
        var token = await CreateInvite(factory);
        var (member, memberEmail) = (NewSub(), NewEmail());
        await Join(factory, member, memberEmail);

        var byAdmin = await Accept(factory, token, NewSub(), ApiFactory.SecondAllowedEmail);
        var byMember = await Accept(factory, token, member, memberEmail);

        Assert.Equal(HttpStatusCode.OK, byAdmin.StatusCode);
        Assert.True((await Json(byAdmin))["isAdmin"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, byMember.StatusCode);
        Assert.Single(factory.Access.Members, stored => stored.Uid == member);
        Assert.Equal(HttpStatusCode.OK, (await Accept(factory, token, NewSub(), NewEmail())).StatusCode);
    }

    [Fact]
    public async Task Accept_needs_a_verified_google_account_and_a_token()
    {
        var token = await CreateInvite(factory);
        var unsigned = await factory.CreateClient().PostAsync("/api/invites/accept",
            System.Net.Http.Json.JsonContent.Create(new { token }));

        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Accept(factory, token, NewSub(), NewEmail(), emailVerified: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Accept(factory, null, NewSub(), NewEmail())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Accept(factory, "not-a-real-token", NewSub(), NewEmail())).StatusCode);
        // The link is still unused.
        Assert.Contains(factory.Access.Invites, invite => invite.Id == Invites.Hash(token));
    }
}
