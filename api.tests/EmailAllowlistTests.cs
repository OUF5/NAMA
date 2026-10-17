using Api.Infrastructure;

namespace Api.Tests;

// AUTH-02: parsing and matching of ALLOWED_EMAILS.
public class EmailAllowlistTests
{
    [Theory]
    [InlineData("owner@example.com", "owner@example.com")]
    [InlineData(" owner@example.com , second@example.com ,", "second@example.com")]
    [InlineData("owner@example.com", "OWNER@Example.com")]
    // In case the deploy action keeps the quotes around the value.
    [InlineData("\"owner@example.com,second@example.com\"", "owner@example.com")]
    [InlineData("\"owner@example.com,second@example.com\"", "second@example.com")]
    public void Listed_email_is_allowed(string allowedEmails, string email)
    {
        Assert.True(new EmailAllowlist(allowedEmails).IsAllowed(email));
    }

    [Theory]
    [InlineData("owner@example.com", "stranger@example.com")]
    [InlineData("owner@example.com", "owner@example.co")]
    [InlineData("owner@example.com", "")]
    [InlineData("owner@example.com", null)]
    [InlineData("", "owner@example.com")]
    [InlineData(null, "owner@example.com")]
    [InlineData(" , ,", "")]
    public void Other_email_is_not_allowed(string? allowedEmails, string? email)
    {
        Assert.False(new EmailAllowlist(allowedEmails).IsAllowed(email));
    }
}
