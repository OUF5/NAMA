namespace Api.Infrastructure;

// AUTH-02: the emails in ALLOWED_EMAILS (comma separated). Matching ignores case.
// An empty list allows nobody.
public sealed class EmailAllowlist(string? allowedEmails)
{
    private readonly HashSet<string> _emails = (allowedEmails ?? "")
        .Split(',')
        // Surrounding quotes are dropped in case the deploy action keeps them in the value.
        .Select(email => email.Trim().Trim('"').Trim())
        .Where(email => email.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsAllowed(string? email) => !string.IsNullOrEmpty(email) && _emails.Contains(email);
}
