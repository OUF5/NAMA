using Api.Domain;
using Api.Infrastructure;

namespace Api.Tests;

// PRD 7.4 documents: YYYY-MM-DD strings, long minor units, camelCase kinds, nameLower.
public class FirestoreMappingTests
{
    [Fact]
    public void Fiscal_year_dates_become_strings_and_come_back_unchanged()
    {
        var fy = FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 10, 17), fiscalStartMonth: 1, payday: 27);

        var document = FirestoreDocuments.ToDocument(fy);

        Assert.Equal(("FY 2026", "2026-01-27", "2027-01-26", 1, 27, false),
            (document.Label, document.StartDate, document.EndDate, document.FiscalStartMonth, document.PaydayDay, document.IsTransition));
        Assert.Equal(12, document.Periods.Count);
        Assert.Equal(("2026-09", "Sep 2026", "2026-09-27", "2026-10-26"),
            (document.Periods[8].Key, document.Periods[8].Label, document.Periods[8].StartDate, document.Periods[8].EndDate));

        var back = FirestoreDocuments.ToFiscalYear("2026-01", document);
        Assert.Equal(fy with { Periods = [] }, back with { Periods = [] });
        Assert.Equal(fy.Periods, back.Periods);
    }

    [Fact]
    public void Profile_comes_back_unchanged()
    {
        var profile = new UserProfile("Owner", "owner@example.com", "Home budget", "SAR", "Asia/Riyadh", 7, 31, true, null);

        Assert.Equal(profile, FirestoreDocuments.ToProfile(FirestoreDocuments.ToDocument(profile)));
    }

    [Fact]
    public void Income_keeps_minor_units_and_open_end()
    {
        var salary = FirestoreDocuments.ToDocument(new IncomeItem("a", IncomeKind.Salary, "Salary", 1_500_050, "2026-01", null));
        var once = FirestoreDocuments.ToDocument(new IncomeItem("b", IncomeKind.Additional, "Bonus", 50_025, "2026-09", "2026-09"));

        Assert.Equal(("salary", "Salary", 1_500_050L, "2026-01", (string?)null),
            (salary.Kind, salary.Label, salary.AmountMinor, salary.FromPeriodKey, salary.ToPeriodKey));
        Assert.Equal(("additional", "2026-09", "2026-09"), (once.Kind, once.FromPeriodKey, once.ToPeriodKey));
    }

    [Fact]
    public void Names_are_stored_with_their_lower_case_form()
    {
        var account = FirestoreDocuments.ToDocument(new BankAccount("a", "Al Rajhi"));
        var category = FirestoreDocuments.ToDocument(new Category("c", "Bills & Utilities", 3));

        Assert.Equal(("Al Rajhi", "al rajhi", false), (account.Name, account.NameLower, account.Archived));
        Assert.Equal(("Bills & Utilities", "bills & utilities", 3, false),
            (category.Name, category.NameLower, category.SortOrder, category.Archived));
    }
}
