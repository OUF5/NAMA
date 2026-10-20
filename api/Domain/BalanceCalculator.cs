namespace Api.Domain;

// R-BAL-01: what an account held at the start of a day, before anything recorded for that day.
// It can be negative (an overdraft or a card).
public sealed record OpeningBalance(long AmountMinor, DateOnly Date);

// R-BAL-02: how each month's income is split across the accounts, from a month on. Shares are basis
// points (1% = 100, so 33.33% is exact) and add up to 10,000.
public sealed record IncomeSplit(string FromPeriodKey, IReadOnlyDictionary<string, int> Shares);

// One amount on a date: an entry (spent) or a transfer.
public sealed record DatedAmount(DateOnly Date, long AmountMinor);

// R-BAL-03: an account's balance today, and what makes it up.
public sealed record AccountBalance(long Opening, long Income, long Spent, long TransfersIn, long TransfersOut)
{
    public long Balance => Opening + Income - Spent + TransfersIn - TransfersOut;
}

// PRD 4.8 bank balances. Amounts are long minor units (R-MON-01). Month keys are YYYY-MM, so ordinal
// order is calendar order.
public static class BalanceCalculator
{
    public const int WholeShare = 10_000;

    // R-BAL-02: the split of a month is the latest one starting in or before it. Months before the
    // first split use the first split. Null when there is no split at all.
    public static IncomeSplit? SplitFor(IReadOnlyList<IncomeSplit> splits, string periodKey) =>
        splits.Where(split => string.CompareOrdinal(split.FromPeriodKey, periodKey) <= 0)
            .MaxBy(split => split.FromPeriodKey, StringComparer.Ordinal)
        ?? splits.MinBy(split => split.FromPeriodKey, StringComparer.Ordinal);

    // R-BAL-02: each share is rounded down, and the minor units left go to the largest share (the
    // first account id on a tie), so the shares always add up to the income exactly.
    public static IReadOnlyDictionary<string, long> Distribute(long amountMinor, IReadOnlyDictionary<string, int> shares)
    {
        var parts = shares.Where(share => share.Value > 0).ToList();
        if (parts.Count == 0)
        {
            return new Dictionary<string, long>();
        }
        var result = parts.ToDictionary(share => share.Key, share => amountMinor * share.Value / WholeShare);
        var largest = parts.OrderByDescending(share => share.Value).ThenBy(share => share.Key, StringComparer.Ordinal).First().Key;
        result[largest] += amountMinor - result.Values.Sum();
        return result;
    }

    // R-BAL-02 (as the salary, R-INC-02): a new split replaces every split from its month on. Returns
    // the months of the splits it replaces, its own month included when one starts there.
    public static IReadOnlyList<string> Replaced(IEnumerable<IncomeSplit> splits, string fromPeriodKey) =>
        splits.Where(split => string.CompareOrdinal(split.FromPeriodKey, fromPeriodKey) >= 0)
            .Select(split => split.FromPeriodKey)
            .ToList();

    // R-BAL-03: the opening balance, plus the account's share of the income of every month that
    // started from the opening date to today (income arrives on payday, a month's first day), minus
    // its entries, plus transfers in, minus transfers out, all dated from the opening date to today.
    public static AccountBalance Balance(
        string accountId,
        OpeningBalance opening,
        IEnumerable<Period> periods,
        IReadOnlyDictionary<string, long> income,
        IReadOnlyList<IncomeSplit> splits,
        IEnumerable<DatedAmount> spent,
        IEnumerable<DatedAmount> transfersIn,
        IEnumerable<DatedAmount> transfersOut,
        DateOnly today)
    {
        bool Counts(DateOnly date) => opening.Date <= date && date <= today;
        long Sum(IEnumerable<DatedAmount> amounts) => amounts.Where(amount => Counts(amount.Date)).Sum(amount => amount.AmountMinor);

        var incomeIn = periods
            .Where(period => Counts(period.StartDate))
            .Sum(period => SplitFor(splits, period.Key) is { } split
                ? Distribute(income.GetValueOrDefault(period.Key), split.Shares).GetValueOrDefault(accountId)
                : 0L);
        return new AccountBalance(opening.AmountMinor, incomeIn, Sum(spent), Sum(transfersIn), Sum(transfersOut));
    }
}
