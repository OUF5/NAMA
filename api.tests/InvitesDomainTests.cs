using Api.Domain;

namespace Api.Tests;

// AUTH-06: the link's secret, its stored hash and its 7-day life.
public class InvitesDomainTests
{
    [Fact]
    public void A_new_token_is_43_base64url_characters_and_never_repeats()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => Invites.NewToken()).ToList();

        Assert.All(tokens, token => Assert.Matches("^[A-Za-z0-9_-]{43}$", token));
        Assert.Equal(100, tokens.Distinct().Count());
    }

    // The SHA-256 test vector for "abc" (FIPS 180-2).
    [Fact]
    public void The_stored_id_is_the_lowercase_hex_sha256_of_the_token()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Invites.Hash("abc"));
    }

    [Fact]
    public void A_link_expires_7_days_after_it_was_created()
    {
        var created = new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

        var expires = Invites.ExpiresAt(created);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero), expires);
        Assert.False(Invites.IsExpired(expires, now: expires.AddTicks(-1)));
        Assert.True(Invites.IsExpired(expires, now: expires));
    }

    // The People list shows dates in the admin's profile time zone (Riyadh is UTC+3).
    [Theory]
    [InlineData("2026-10-24T20:59:59Z", "2026-10-24")]
    [InlineData("2026-10-24T21:00:00Z", "2026-10-25")]
    public void A_moment_becomes_a_date_in_the_profile_time_zone(string instant, string expected)
    {
        var date = FiscalCalendar.DateIn(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture), "Asia/Riyadh");

        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), date);
    }
}
