using Api.Features.Overview;
using Api.Infrastructure;

namespace Api.Tests;

// Owner note 9: Tracker's month in detail, in minor units (R-MON-01).
public class MonthBreakdownTests
{
    private static Entry E(string id, int month, int day, string category, string account, long amount) =>
        new(id, new DateOnly(2026, month, day), "2026-09", "2026-01", category, category + "-sub", account, amount, "");

    private static readonly DateOnly[] Days = [new(2026, 9, 29), new(2026, 9, 30), new(2026, 10, 1)];

    // Newest first, as the FY query returns them.
    private static readonly Entry[] Sep =
    [
        E("e", 10, 1, "food", "rajhi", 79_999),
        E("d", 10, 1, "transport", "bank", 10_000),
        E("c", 9, 30, "food", "bank", 40_000),
        E("b", 9, 29, "food", "bank", 1_000),
    ];

    private static readonly Entry[] Aug = [E("a", 9, 20, "food", "bank", 5_000), E("z", 9, 1, "home", "bank", 2_000)];

    [Fact]
    public void Adds_up_the_month_by_day_account_and_category()
    {
        var breakdown = MonthBreakdownCalculator.For(Days, Sep, Aug);

        Assert.Equal((130_999L, 4), (breakdown.Total, breakdown.Count));
        Assert.Equal([1_000L, 40_000L, 89_999L], breakdown.Days.Select(day => day.Spent));
        Assert.Equal([("rajhi", 79_999L), ("bank", 51_000L)], breakdown.Accounts.Select(account => (account.BankAccountId, account.Spent)));
        // Home had spending only last month, so it is listed with 0 this month.
        Assert.Equal(
            [("food", 120_999L, 5_000L), ("transport", 10_000L, 0L), ("home", 0L, 2_000L)],
            breakdown.Categories.Select(change => (change.CategoryId, change.Spent, change.LastSpent)));
    }

    [Fact]
    public void Lists_the_newest_and_the_biggest_entries()
    {
        var many = Sep.Concat([E("f", 9, 29, "food", "bank", 500), E("g", 9, 29, "food", "bank", 10_000)]).ToList();

        var breakdown = MonthBreakdownCalculator.For(Days, many, []);

        Assert.Equal(["e", "d", "c", "b", "f"], breakdown.Latest.Select(entry => entry.Id));
        // d and g tie at 100: d is newer, so it comes first.
        Assert.Equal(["e", "c", "d", "g", "b"], breakdown.Biggest.Select(entry => entry.Id));
    }

    [Fact]
    public void An_empty_month_has_every_day_at_zero()
    {
        var breakdown = MonthBreakdownCalculator.For(Days, [], []);

        Assert.Equal((0L, 0), (breakdown.Total, breakdown.Count));
        Assert.All(breakdown.Days, day => Assert.Equal(0, day.Spent));
        Assert.Empty(breakdown.Accounts);
        Assert.Empty(breakdown.Categories);
        Assert.Empty(breakdown.Latest);
    }
}
