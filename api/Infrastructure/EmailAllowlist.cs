using System.Security.Claims;

namespace Api.Infrastructure;

// AUTH-02: the admins, the emails in ALLOWED_EMAILS (comma separated). Matching ignores case.
// An empty list has no admins. Everyone else needs an invite link (AUTH-06).
public sealed class EmailAllowlist
{
    private readonly HashSet<string> _emails;

    public EmailAllowlist(string? allowedEmails)
    {
        Emails = (allowedEmails ?? "")
            .Split(',')
            // Surrounding quotes are dropped in case the deploy action keeps them in the value.
            .Select(email => email.Trim().Trim('"').Trim())
            .Where(email => email.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _emails = Emails.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // In the order they are listed, for Settings → People (AUTH-07).
    public IReadOnlyList<string> Emails { get; }

    public bool IsAllowed(string? email) => !string.IsNullOrEmpty(email) && _emails.Contains(email);

    // Every route already requires a verified email (FirebaseAuth), so the email claim can be trusted here.
    public bool IsAdmin(ClaimsPrincipal user) => IsAllowed(user.FindFirstValue("email"));
}
