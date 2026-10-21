using Api.Infrastructure;

namespace Api.Features.Overview;

public sealed record DaySpent(DateOnly Date, long Spent);

public sealed record AccountSpent(string BankAccountId, long Spent);

// A category's spending this month and in the month before.
public sealed record CategoryChange(string CategoryId, long Spent, long LastSpent);

// Owner note 9: one month on Tracker in detail. Days has every day of the month (0 when nothing was
// spent); Accounts and Categories are largest first; Latest and Biggest hold at most ListSize entries.
public sealed record MonthBreakdown(
    long Total,
    int Count,
    IReadOnlyList<DaySpent> Days,
    IReadOnlyList<AccountSpent> Accounts,
    IReadOnlyList<CategoryChange> Categories,
    IReadOnlyList<Entry> Latest,
    IReadOnlyList<Entry> Biggest);

// Sums in minor units (R-MON-01). The entries come newest first, as the FY query returns them
// (TBL-01), so Latest keeps that order and Biggest breaks ties the same way.
public static class MonthBreakdownCalculator
{
    public const int ListSize = 5;

    public static MonthBreakdown For(IReadOnlyList<DateOnly> days, IReadOnlyList<Entry> entries, IReadOnlyList<Entry> lastMonth)
    {
        var byDay = entries.GroupBy(entry => entry.Date).ToDictionary(group => group.Key, group => group.Sum(entry => entry.AmountMinor));
        var last = lastMonth.GroupBy(entry => entry.CategoryId).ToDictionary(group => group.Key, group => group.Sum(entry => entry.AmountMinor));
        var now = entries.GroupBy(entry => entry.CategoryId).ToDictionary(group => group.Key, group => group.Sum(entry => entry.AmountMinor));
        return new MonthBreakdown(
            entries.Sum(entry => entry.AmountMinor),
            entries.Count,
            days.Select(day => new DaySpent(day, byDay.GetValueOrDefault(day))).ToList(),
            entries
                .GroupBy(entry => entry.BankAccountId)
                .Select(group => new AccountSpent(group.Key, group.Sum(entry => entry.AmountMinor)))
                .OrderByDescending(account => account.Spent)
                .ThenBy(account => account.BankAccountId, StringComparer.Ordinal)
                .ToList(),
            now.Keys.Union(last.Keys)
                .Select(id => new CategoryChange(id, now.GetValueOrDefault(id), last.GetValueOrDefault(id)))
                .OrderByDescending(change => change.Spent)
                .ThenByDescending(change => change.LastSpent)
                .ThenBy(change => change.CategoryId, StringComparer.Ordinal)
                .ToList(),
            entries.Take(ListSize).ToList(),
            entries.OrderByDescending(entry => entry.AmountMinor).Take(ListSize).ToList());
    }
}
