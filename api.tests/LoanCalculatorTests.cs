using System.Globalization;
using Api.Domain;

namespace Api.Tests;

// PRD 4.10 loans (owner note 20): totals and By person, in minor units.
public class LoanCalculatorTests
{
    private static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Loan Loan(string id, string name, LoanType type, long amount, string date, string? settledOn = null) =>
        new(id, name, type, amount, Date(date), "snb", "", settledOn is null ? null : Date(settledOn));

    private static readonly Loan[] Loans =
    [
        Loan("a", "Ahmed", LoanType.Lent, 100_000, "2026-09-01"),
        Loan("b", "ahmed ", LoanType.Lent, 50_000, "2026-10-01"),
        Loan("c", "Ahmed", LoanType.Borrowed, 20_000, "2026-10-05"),
        Loan("d", "Sara", LoanType.Borrowed, 300_000, "2025-06-01"),
        Loan("e", "Omar", LoanType.Lent, 70_000, "2026-08-01", settledOn: "2026-09-15"),
        // A date to come counts too (R-LON-01: everything counts).
        Loan("f", "Sara", LoanType.Lent, 1_001, "2027-03-01"),
    ];

    // R-LON-04
    [Fact]
    public void Totals_add_up_open_and_settled_apart()
    {
        var totals = LoanCalculator.Totals(Loans);

        Assert.Equal(new LoanTypeTotals(151_001, 3, 70_000, 1), totals.Lent);
        Assert.Equal(new LoanTypeTotals(320_000, 2, 0, 0), totals.Borrowed);
        Assert.Equal(-168_999, totals.Net);
    }

    [Fact]
    public void Totals_of_no_loans_are_zero()
    {
        var totals = LoanCalculator.Totals([]);

        Assert.Equal((new LoanTypeTotals(0, 0, 0, 0), new LoanTypeTotals(0, 0, 0, 0), 0L), (totals.Lent, totals.Borrowed, totals.Net));
    }

    // LON-03: names grouped ignoring case, as written on the newest loan; settled loans left out.
    [Fact]
    public void By_person_groups_open_loans_by_name()
    {
        Assert.Equal(
            [
                new LoanPerson("Ahmed", 150_000, 20_000, 3),
                new LoanPerson("Sara", 1_001, 300_000, 2),
            ],
            LoanCalculator.ByPerson(Loans));
    }

    // R-LON-03
    [Theory]
    [InlineData("2026-09-30", false)]
    [InlineData("2026-10-01", true)]
    [InlineData("2027-01-01", true)]
    public void A_loan_is_settled_on_or_after_its_date(string date, bool expected)
    {
        Assert.Equal(expected, LoanCalculator.CanSettleOn(Loans[1], Date(date)));
    }
}
