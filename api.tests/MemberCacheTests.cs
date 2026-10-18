using System.Net;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// AUTH-02 without a Firestore read on every request: the members are read once and kept in memory
// (Cloud Run max instances = 1), joins and removals change that set, and it is read again at most
// every 5 minutes. Each test has its own API. The People list in an answer (creating a link,
// removing a person) is a separate read, so the counts are taken after those calls.
public class MemberCacheTests
{
    private const string Email = "friend@example.com";

    [Fact]
    public async Task A_members_requests_read_nothing_after_the_first_check()
    {
        using var factory = new ApiFactory();
        var sub = NewSub();
        // The accept checks whether the caller is already allowed: that loads the set.
        await Join(factory, sub, Email);
        var loads = factory.Access.MemberLoads;

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Send(factory, HttpMethod.Get, "/api/me", sub, email: Email)).StatusCode);
        }

        Assert.Equal(loads, factory.Access.MemberLoads);
    }

    [Fact]
    public async Task The_members_are_read_again_after_5_minutes()
    {
        using var factory = new ApiFactory();
        var sub = NewSub();
        await Join(factory, sub, Email);
        var loads = factory.Access.MemberLoads;

        factory.Time.UtcNow = TestTime.Default.AddMinutes(5).AddSeconds(-1);
        await Send(factory, HttpMethod.Get, "/api/me", sub, email: Email);
        Assert.Equal(loads, factory.Access.MemberLoads);

        factory.Time.UtcNow = TestTime.Default.AddMinutes(5);
        await Send(factory, HttpMethod.Get, "/api/me", sub, email: Email);
        await Send(factory, HttpMethod.Get, "/api/me", sub, email: Email);
        Assert.Equal(loads + 1, factory.Access.MemberLoads);
    }

    // Admins can never be locked out, even when Firestore's members cannot be read.
    [Fact]
    public async Task Admin_requests_never_read_the_members()
    {
        using var factory = new ApiFactory();
        factory.Access.FailMemberLoads = true;

        var me = await Send(factory, HttpMethod.Get, "/api/me", NewSub());
        var other = await Send(factory, HttpMethod.Get, "/api/me", NewSub(), email: ApiFactory.SecondAllowedEmail);
        var accounts = await Send(factory, HttpMethod.Get, "/api/bank-accounts", NewSub());

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (me.StatusCode, other.StatusCode, accounts.StatusCode));
        Assert.True((await Json(me))["isAdmin"]!.GetValue<bool>());
        Assert.Equal(0, factory.Access.MemberLoads);
    }

    [Fact]
    public async Task Joining_and_removing_change_the_set_without_a_read()
    {
        using var factory = new ApiFactory();
        var (first, second, secondEmail) = (NewSub(), NewSub(), "second-friend@example.com");
        await Join(factory, first, Email);
        var secondId = await Join(factory, second, secondEmail);
        Assert.Equal(HttpStatusCode.OK, (await Send(factory, HttpMethod.Get, "/api/me", second, email: secondEmail)).StatusCode);

        await Send(factory, HttpMethod.Delete, $"/api/people/{secondId}", NewSub());
        var loads = factory.Access.MemberLoads;

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(factory, HttpMethod.Get, "/api/me", second, email: secondEmail)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(factory, HttpMethod.Get, "/api/me", first, email: Email)).StatusCode);
        Assert.Equal(loads, factory.Access.MemberLoads);
    }
}
