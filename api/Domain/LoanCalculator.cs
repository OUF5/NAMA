namespace Api.Domain;

// R-LON-01: Lent (you gave the money; they owe you) or Borrowed (you received it; you owe).
public enum LoanType { Lent, Borrowed }

// R-LON-01 to R-LON-03. SettledOn is null while the loan is open. Information only (R-LON-02): nothing
// else in NAMA reads a loan.
public sealed record Loan(
    string Id,
    string Name,
    LoanType Type,
    long AmountMinor,
    DateOnly Date,
    string BankAccountId,
    string Note,
    DateOnly? SettledOn = null)
{
    public bool IsOpen => SettledOn is null;
}

// One type's totals: the open loans and the settled ones, added up apart.
public sealed record LoanTypeTotals(long Open, int OpenCount, long Settled, int SettledCount);

// R-LON-04: Net is Lent − Borrowed of the open loans (above 0: you are owed more than you owe).
public sealed record LoanTotals(LoanTypeTotals Lent, LoanTypeTotals Borrowed)
{
    public long Net => Lent.Open - Borrowed.Open;
}

// LON-03 By person: one name's open loans.
public sealed record LoanPerson(string Name, long LentOpen, long BorrowedOpen, int OpenCount);

// PRD 4.10 loans. Amounts are long minor units (R-MON-01). Every loan counts, whatever its date.
public static class LoanCalculator
{
    public static LoanTotals Totals(IEnumerable<Loan> loans)
    {
        var list = loans.ToList();
        return new LoanTotals(TypeTotals(list, LoanType.Lent), TypeTotals(list, LoanType.Borrowed));
    }

    // Names are grouped ignoring case (R-LON-01 lets them repeat), shown as written on the newest loan,
    // and sorted by name. Only names with an open loan are listed.
    public static IReadOnlyList<LoanPerson> ByPerson(IEnumerable<Loan> loans) =>
        loans.Where(loan => loan.IsOpen)
            .GroupBy(loan => Names.Lower(loan.Name), StringComparer.Ordinal)
            .Select(group => new LoanPerson(
                group.OrderByDescending(loan => loan.Date).ThenBy(loan => loan.Id, StringComparer.Ordinal).First().Name,
                group.Where(loan => loan.Type == LoanType.Lent).Sum(loan => loan.AmountMinor),
                group.Where(loan => loan.Type == LoanType.Borrowed).Sum(loan => loan.AmountMinor),
                group.Count()))
            .OrderBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    // R-LON-03: a loan is settled on or after the day it was made.
    public static bool CanSettleOn(Loan loan, DateOnly date) => date >= loan.Date;

    private static LoanTypeTotals TypeTotals(IReadOnlyList<Loan> loans, LoanType type)
    {
        var ofType = loans.Where(loan => loan.Type == type).ToList();
        var open = ofType.Where(loan => loan.IsOpen).ToList();
        var settled = ofType.Where(loan => !loan.IsOpen).ToList();
        return new LoanTypeTotals(open.Sum(loan => loan.AmountMinor), open.Count, settled.Sum(loan => loan.AmountMinor), settled.Count);
    }
}
