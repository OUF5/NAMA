namespace Api.Domain;

// R-MON-01: amounts are stored as long minor units (125.50 is 12550) and are decimal in DTOs.
public static class Money
{
    public const decimal MaxAmount = 10_000_000m;

    // R-MON-03: chosen in the wizard, formatting only. The list is the owner's choice (M3).
    public static readonly IReadOnlyList<string> Currencies = ["SAR", "QAR", "AED", "USD", "EUR", "GBP"];

    public static long ToMinor(decimal amount)
    {
        if (!HasAtMostTwoDecimals(amount))
        {
            throw new ArgumentException("An amount has at most 2 decimals.", nameof(amount));
        }
        return decimal.ToInt64(amount * 100);
    }

    public static decimal FromMinor(long minor) => minor / 100m;

    // R-MON-02: greater than 0, at most 2 decimals, at most 10,000,000. SET-01 allows a salary of 0.
    // The range is checked first, so a huge amount cannot overflow the decimals check.
    public static bool IsValidAmount(decimal amount, bool allowZero = false) =>
        (allowZero ? amount >= 0 : amount > 0) && amount <= MaxAmount && HasAtMostTwoDecimals(amount);

    // R-BAL-01: an opening balance may be 0 or negative (an overdraft or a card), at most
    // 10,000,000 either way, with at most 2 decimals.
    public static bool IsValidBalance(decimal amount) => Math.Abs(amount) <= MaxAmount && HasAtMostTwoDecimals(amount);

    // R-BAL-02: a share of income is 0 to 100 percent with at most 2 decimals, stored as basis points
    // (80.5% is 8050), so it stays an exact whole number.
    public static bool IsValidPercent(decimal percent) => percent >= 0 && percent <= 100 && HasAtMostTwoDecimals(percent);

    public static int ToBasisPoints(decimal percent) => decimal.ToInt32(percent * 100);

    public static decimal FromBasisPoints(int basisPoints) => basisPoints / 100m;

    // Not decimal.Scale: 1.500m has scale 3 but only 2 real decimals.
    private static bool HasAtMostTwoDecimals(decimal amount) => amount * 100 == decimal.Truncate(amount * 100);
}
