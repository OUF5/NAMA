using Api.Domain;

namespace Api.Tests;

// PRD 4.8 bank balances (R-BAL-01 to R-BAL-03). Amounts are minor units; shares are basis points.
public class BalanceCalculatorTests
{
    // FY 2026 of the section 3 example: payday 27, Jan 2026 (27 Jan to 26 Feb) to Dec 2026.
    private static readonly FiscalYear Fy = FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 10, 17), 1, 27);

    private static readonly Dictionary<string, int> MainAndSavings = new() { ["main"] = 8_000, ["savings"] = 2_000 };

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void Income_is_split_by_the_shares()
    {
        Assert.Equal(new Dictionary<string, long> { ["main"] = 1_360_000, ["savings"] = 340_000 },
            BalanceCalculator.Distribute(1_700_000, MainAndSavings));
    }

    // The minor units left after rounding down go to the largest share, so nothing is lost.
    [Fact]
    public void Rounding_never_loses_a_minor_unit()
    {
        Assert.Equal(new Dictionary<string, long> { ["main"] = 1_360_001, ["savings"] = 340_000 },
            BalanceCalculator.Distribute(1_700_001, MainAndSavings));

        var thirds = BalanceCalculator.Distribute(100, new Dictionary<string, int> { ["a"] = 3_333, ["b"] = 3_333, ["c"] = 3_334 });
        Assert.Equal(new Dictionary<string, long> { ["a"] = 33, ["b"] = 33, ["c"] = 34 }, thirds);
        Assert.Equal(100, thirds.Values.Sum());
    }

    // A tie goes to the first account id, so the result never depends on the order of the map.
    [Fact]
    public void A_tie_goes_to_the_first_account_id()
    {
        Assert.Equal(new Dictionary<string, long> { ["a"] = 1, ["b"] = 0 },
            BalanceCalculator.Distribute(1, new Dictionary<string, int> { ["b"] = 5_000, ["a"] = 5_000 }));
    }

    [Fact]
    public void A_zero_share_gets_nothing_and_no_shares_give_nothing()
    {
        Assert.Equal(new Dictionary<string, long> { ["main"] = 500 },
            BalanceCalculator.Distribute(500, new Dictionary<string, int> { ["main"] = 10_000, ["savings"] = 0 }));
        Assert.Empty(BalanceCalculator.Distribute(500, new Dictionary<string, int>()));
    }

    // R-BAL-02: the latest split starting in or before the month; before the first one, the first one.
    [Theory]
    [InlineData("2025-12", "2026-01")]
    [InlineData("2026-01", "2026-01")]
    [InlineData("2026-05", "2026-01")]
    [InlineData("2026-06", "2026-06")]
    [InlineData("2027-03", "2026-06")]
    public void The_split_of_a_month(string periodKey, string expected)
    {
        IncomeSplit[] splits =
        [
            new("2026-06", new Dictionary<string, int> { ["savings"] = 10_000 }),
            new("2026-01", MainAndSavings),
        ];

        Assert.Equal(expected, BalanceCalculator.SplitFor(splits, periodKey)!.FromPeriodKey);
    }

    [Fact]
    public void No_split_at_all_is_null()
    {
        Assert.Null(BalanceCalculator.SplitFor([], "2026-01"));
    }

    // As the salary (R-INC-02): a new split replaces every split from its month on.
    [Fact]
    public void A_new_split_replaces_the_ones_from_its_month_on()
    {
        IncomeSplit[] splits = [new("2026-01", MainAndSavings), new("2026-06", MainAndSavings), new("2026-09", MainAndSavings)];

        Assert.Equal(["2026-06", "2026-09"], BalanceCalculator.Replaced(splits, "2026-06"));
        Assert.Equal(["2026-09"], BalanceCalculator.Replaced(splits, "2026-07"));
        Assert.Empty(BalanceCalculator.Replaced(splits, "2026-10"));
    }

    // The owner's example: Main 5,000 and Savings 20,000 at the start of the FY, income 17,000 split
    // 80 / 20, 14,000 spent from Main and 500 moved to Savings in Jan 2026. On 26 Feb 2026 (the last day
    // of Jan 2026) Main has 5,000 + 13,600 - 14,000 - 500 = 4,100 and Savings 20,000 + 3,400 + 500 = 23,900.
    [Fact]
    public void Worked_example_balances()
    {
        var income = Fy.Periods.ToDictionary(period => period.Key, _ => 1_700_000L);
        IncomeSplit[] splits = [new("2026-01", MainAndSavings)];
        DatedAmount[] spent = [new(D(2026, 2, 1), 1_000_000), new(D(2026, 2, 20), 400_000)];
        DatedAmount[] moved = [new(D(2026, 2, 10), 50_000)];
        var today = D(2026, 2, 26);

        var main = BalanceCalculator.Balance("main", new OpeningBalance(500_000, Fy.StartDate), Fy.Periods, income, splits,
            spent, transfersIn: [], transfersOut: moved, today);
        var savings = BalanceCalculator.Balance("savings", new OpeningBalance(2_000_000, Fy.StartDate), Fy.Periods, income, splits,
            spent: [], transfersIn: moved, transfersOut: [], today);

        Assert.Equal(new AccountBalance(500_000, 1_360_000, 1_400_000, 0, 50_000), main);
        Assert.Equal(410_000, main.Balance);
        Assert.Equal(new AccountBalance(2_000_000, 340_000, 0, 50_000, 0), savings);
        Assert.Equal(2_390_000, savings.Balance);
    }

    // R-BAL-01, set up partway through a month: on 20 Oct 2026 (Sep 2026 runs 27 Sep to 26 Oct) the
    // bank shows 3,000. Sep's salary and the spending before 20 Oct are already in it, so they do not
    // count again. What is dated 20 Oct or later counts, and Oct's salary counts once 27 Oct comes.
    [Fact]
    public void Only_what_comes_on_or_after_the_opening_date_counts()
    {
        var income = Fy.Periods.ToDictionary(period => period.Key, _ => 1_000_000L);
        IncomeSplit[] splits = [new("2026-01", new Dictionary<string, int> { ["main"] = 10_000 })];
        var opening = new OpeningBalance(300_000, D(2026, 10, 20));
        DatedAmount[] spent = [new(D(2026, 10, 19), 9_999), new(D(2026, 10, 20), 10_000), new(D(2026, 10, 30), 20_000)];

        long On(DateOnly today) => BalanceCalculator.Balance("main", opening, Fy.Periods, income, splits, spent, [], [], today).Balance;

        Assert.Equal(290_000, On(D(2026, 10, 20)));
        // Oct 2026 starts on 27 Oct: its 10,000 arrives. The entry dated 30 Oct is still to come.
        Assert.Equal(1_290_000, On(D(2026, 10, 27)));
        Assert.Equal(1_270_000, On(D(2026, 10, 30)));
    }

    // A balance can go below 0 (an overdraft), and an opening balance can be negative (a card).
    [Fact]
    public void Balances_can_be_negative()
    {
        var balance = BalanceCalculator.Balance("card", new OpeningBalance(-50_000, Fy.StartDate), Fy.Periods,
            new Dictionary<string, long>(), [], [new(D(2026, 3, 1), 20_000)], [], [], D(2026, 3, 5));

        Assert.Equal(-70_000, balance.Balance);
    }

    // An account with no share gets no income, and without any split no account does.
    [Fact]
    public void Without_a_share_no_income_arrives()
    {
        var income = Fy.Periods.ToDictionary(period => period.Key, _ => 1_000_000L);
        var opening = new OpeningBalance(0, Fy.StartDate);

        Assert.Equal(0, BalanceCalculator.Balance("other", opening, Fy.Periods, income, [new("2026-01", MainAndSavings)], [], [], [], D(2026, 12, 31)).Income);
        Assert.Equal(0, BalanceCalculator.Balance("main", opening, Fy.Periods, income, [], [], [], [], D(2026, 12, 31)).Income);
    }

    // R-BAL-03, R-INC-05 (owner note 17): income that goes into one account arrives on payday, like
    // the account's share of the split.
    [Fact]
    public void Income_into_one_account_arrives_on_payday()
    {
        var opening = new OpeningBalance(100_000, D(2026, 3, 1));
        var direct = new Dictionary<string, long> { ["2026-03"] = 50_000 };

        long On(DateOnly today) => BalanceCalculator.Balance("main", opening, Fy.Periods, new Dictionary<string, long>(), [],
            [], [], [], today, direct).Balance;

        // Mar 2026 starts on 27 Mar.
        Assert.Equal(100_000, On(D(2026, 3, 26)));
        Assert.Equal(150_000, On(D(2026, 3, 27)));
    }
}
