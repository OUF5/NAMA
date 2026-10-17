namespace Api.Domain;

// PRD 4.7: names are trimmed, 1 to 40 characters, and unique ignoring case within their group.
public static class Names
{
    public const int MaxLength = 40;

    public static string Normalize(string? name) => (name ?? "").Trim();

    public static bool IsValid(string? name) => Normalize(name).Length is >= 1 and <= MaxLength;

    // Stored as nameLower, so uniqueness ignores case (PRD 7.4).
    public static string Lower(string? name) => Normalize(name).ToLowerInvariant();

    public static bool AreUnique(IEnumerable<string?> names)
    {
        var lower = names.Select(Lower).ToList();
        return lower.Distinct(StringComparer.Ordinal).Count() == lower.Count;
    }
}
