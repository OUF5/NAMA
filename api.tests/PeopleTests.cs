using System.Net;
using System.Text.Json.Nodes;
using Api.Domain;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// AUTH-07, STG-07: admins see the admins, who joined and the unused links, and can revoke a link or
// remove a person. A removed person loses access at once and keeps their data.
public class PeopleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string NewEmail() => $"friend-{Guid.NewGuid():N}@example.com";

    private async Task<JsonNode> People(string adminSub)
    {
        var response = await Send(factory, HttpMethod.Get, "/api/settings", adminSub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response))["people"]!;
    }

    [Fact]
    public async Task Settings_lists_the_admins_who_joined_and_the_unused_links()
    {
        var admin = await SetUpUser(factory);
        var (sub, email) = (NewSub(), NewEmail());
        var memberId = await Join(factory, sub, email);
        var token = await CreateInvite(factory, admin);

        var people = await People(admin);

        Assert.Equal([ApiFactory.AllowedEmail, ApiFactory.SecondAllowedEmail], people["admins"]!.AsArray().Select(a => a!.GetValue<string>()));
        var member = people["members"]!.AsArray().Single(m => m!["id"]!.GetValue<string>() == memberId)!;
        Assert.Equal((email, "2026-10-17"), (member["email"]!.GetValue<string>(), member["joinedOn"]!.GetValue<string>()));
        Assert.Null(member["uid"]);
        var invite = people["invites"]!.AsArray().Single(i => i!["id"]!.GetValue<string>() == Invites.Hash(token))!;
        Assert.False(invite["expired"]!.GetValue<bool>());

        // 8 days later the link is listed as expired until it is revoked.
        factory.Time.UtcNow = TestTime.Default.AddDays(8);
        try
        {
            var later = (await People(admin))["invites"]!.AsArray().Single(i => i!["id"]!.GetValue<string>() == Invites.Hash(token))!;
            Assert.True(later["expired"]!.GetValue<bool>());
        }
        finally
        {
            factory.Time.UtcNow = TestTime.Default;
        }
    }

    [Fact]
    public async Task A_member_sees_no_people_in_settings()
    {
        var (sub, email) = (NewSub(), NewEmail());
        await Join(factory, sub, email);
        await SetUpUser(factory, sub: sub);

        var settings = await Send(factory, HttpMethod.Get, "/api/settings", sub, email: email);
        var me = await Send(factory, HttpMethod.Get, "/api/me", sub, email: email);

        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Null((await Json(settings))["people"]);
        Assert.False((await Json(me))["isAdmin"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Removing_a_person_ends_their_access_at_once_and_keeps_their_data()
    {
        var (sub, email) = (NewSub(), NewEmail());
        var memberId = await Join(factory, sub, email);
        await SetUpUser(factory, sub: sub);
        Assert.Equal(HttpStatusCode.OK, (await Send(factory, HttpMethod.Get, "/api/me", sub, email: email)).StatusCode);
        var loads = factory.Access.MemberLoads;

        var removed = await Send(factory, HttpMethod.Delete, $"/api/people/{memberId}", NewSub());

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.DoesNotContain((await Json(removed))["people"]!["members"]!.AsArray(), m => m!["id"]!.GetValue<string>() == memberId);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(factory, HttpMethod.Get, "/api/me", sub, email: email)).StatusCode);
        // The access check read nothing (the one read is the People list in the answer), and their data is untouched.
        Assert.Equal(loads + 1, factory.Access.MemberLoads);
        Assert.NotNull(await factory.Store.GetProfileAsync(sub));
        Assert.Equal(2, (await factory.Store.GetBankAccountsAsync(sub)).Count);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(factory, HttpMethod.Delete, $"/api/people/{memberId}", NewSub())).StatusCode);
    }

    [Fact]
    public async Task A_removed_person_invited_again_gets_their_data_back()
    {
        var (sub, email) = (NewSub(), NewEmail());
        var memberId = await Join(factory, sub, email);
        await SetUpUser(factory, sub: sub);
        await Send(factory, HttpMethod.Delete, $"/api/people/{memberId}", NewSub());

        var again = await Accept(factory, await CreateInvite(factory), sub, email);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var me = await Json(again);
        Assert.True(me["setupCompleted"]!.GetValue<bool>());
        Assert.Equal("Home budget", me["profile"]!["projectName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Only_admins_manage_links_and_people()
    {
        var (member, memberEmail) = (NewSub(), NewEmail());
        var memberId = await Join(factory, member, memberEmail);
        var id = Invites.Hash(await CreateInvite(factory));

        foreach (var (sub, email) in new[] { (member, memberEmail), (NewSub(), NewEmail()) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(factory, HttpMethod.Post, "/api/invites", sub, email: email)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(factory, HttpMethod.Delete, $"/api/invites/{id}", sub, email: email)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(factory, HttpMethod.Delete, $"/api/people/{memberId}", sub, email: email)).StatusCode);
        }
        Assert.Contains(factory.Access.Invites, invite => invite.Id == id);
        Assert.Contains(factory.Access.Members, stored => stored.Id == memberId);
    }

    [Theory]
    [InlineData("POST", "/api/invites")]
    [InlineData("DELETE", "/api/invites/x")]
    [InlineData("DELETE", "/api/people/x")]
    [InlineData("POST", "/api/invites/accept")]
    public async Task Calls_without_a_token_get_401(string method, string url)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
