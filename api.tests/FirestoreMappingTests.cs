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

    // R-LON-01 to R-LON-03: lent or borrowed, minor units, and a settled date or none.
    [Fact]
    public void Loan_comes_back_unchanged()
    {
        var open = new Loan("l1", "Ahmed", LoanType.Borrowed, 123_456, new DateOnly(2025, 6, 1), "bank", "Car");
        var settled = open with { Type = LoanType.Lent, SettledOn = new DateOnly(2026, 10, 23) };

        Assert.Equal(("borrowed", "2025-06-01", (string?)null), (FirestoreDocuments.ToDocument(open).Type,
            FirestoreDocuments.ToDocument(open).Date, FirestoreDocuments.ToDocument(open).SettledOn));
        Assert.Equal(open, FirestoreDocuments.ToLoan("l1", FirestoreDocuments.ToDocument(open)));
        Assert.Equal(("lent", "2026-10-23"), (FirestoreDocuments.ToDocument(settled).Type, FirestoreDocuments.ToDocument(settled).SettledOn));
        Assert.Equal(settled, FirestoreDocuments.ToLoan("l1", FirestoreDocuments.ToDocument(settled)));
    }

    // R-CAL-11: stored as "end" or "payday"; a profile saved before 23 Oct 2026 has none and is named
    // by the month each month ends in.
    [Fact]
    public void Month_naming_comes_back_unchanged_and_defaults_to_the_month_it_ends_in()
    {
        var profile = new UserProfile("Owner", "owner@example.com", "Home budget", "SAR", "Asia/Riyadh", 1, 25, true, null,
            MonthNaming: MonthNaming.PaydayMonth);
        var document = FirestoreDocuments.ToDocument(profile);

        Assert.Equal("payday", document.MonthNaming);
        Assert.Equal(profile, FirestoreDocuments.ToProfile(document));
        Assert.Equal("end", FirestoreDocuments.ToDocument(profile with { MonthNaming = MonthNaming.EndMonth }).MonthNaming);
        document.MonthNaming = null;
        Assert.Equal(MonthNaming.EndMonth, FirestoreDocuments.ToProfile(document).MonthNaming);
    }

    // R-PLN-06: the day setup was finished is createdAt in the profile's time zone (21:30 UTC on 20 Oct
    // is already 21 Oct in Riyadh). A document never stored has none.
    [Fact]
    public void The_start_day_comes_from_created_at_in_the_profile_time_zone()
    {
        var profile = new UserProfile("Owner", "owner@example.com", "Home budget", "SAR", "Asia/Riyadh", 1, 1, true, null);
        var document = FirestoreDocuments.ToDocument(profile);
        Assert.Null(FirestoreDocuments.ToProfile(document).StartedOn);

        document.CreatedAt = Google.Cloud.Firestore.Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 10, 20, 21, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 21), FirestoreDocuments.ToProfile(document).StartedOn);
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

    // R-BAL-01: the opening balance is long minor units and a YYYY-MM-DD date; both null when unset.
    [Fact]
    public void Bank_account_keeps_its_opening_balance()
    {
        var account = new BankAccount("a", "Card", Opening: new OpeningBalance(-120_050, new DateOnly(2026, 10, 20)));

        var document = FirestoreDocuments.ToDocument(account);

        Assert.Equal((-120_050L, "2026-10-20"), (document.OpeningBalanceMinor, document.OpeningDate));
        Assert.Equal(account, FirestoreDocuments.ToBankAccount("a", document));
        var unset = FirestoreDocuments.ToDocument(new BankAccount("b", "Old"));
        Assert.Equal(((long?)null, (string?)null), (unset.OpeningBalanceMinor, unset.OpeningDate));
        Assert.Null(FirestoreDocuments.ToBankAccount("b", unset).Opening);
    }

    // R-BAL-02: incomeSplits/{fromPeriodKey}, shares in basis points.
    [Fact]
    public void Income_split_comes_back_unchanged()
    {
        var split = new IncomeSplit("2026-03", new Dictionary<string, int> { ["a"] = 8_050, ["b"] = 1_950 });

        var document = FirestoreDocuments.ToDocument(split);

        Assert.Equal(("2026-03", 8_050L), (document.FromPeriodKey, document.Shares["a"]));
        var back = FirestoreDocuments.ToIncomeSplit(document);
        Assert.Equal(split.FromPeriodKey, back.FromPeriodKey);
        Assert.Equal(split.Shares, back.Shares);
    }

    // R-TRF-01: as an entry, with the two accounts.
    [Fact]
    public void Transfer_comes_back_unchanged()
    {
        var transfer = new Transfer("t", new DateOnly(2026, 10, 18), "2026-09", "2026-01", "a", "b", 50_025, "Savings");

        var document = FirestoreDocuments.ToDocument(transfer);

        Assert.Equal(("2026-10-18", "a", "b", 50_025L), (document.Date, document.FromBankAccountId, document.ToBankAccountId, document.AmountMinor));
        Assert.Equal(transfer, FirestoreDocuments.ToTransfer("t", document));
    }

    [Fact]
    public void Categories_and_sub_categories_come_back_unchanged()
    {
        var category = new Category("c", "Food", 2, Archived: true);
        var sub = new SubCategory("s", "c", "Dining Out", 1);

        var subDocument = FirestoreDocuments.ToDocument(sub);

        Assert.Equal(("c", "dining out"), (subDocument.CategoryId, subDocument.NameLower));
        Assert.Equal(category, FirestoreDocuments.ToCategory("c", FirestoreDocuments.ToDocument(category)));
        Assert.Equal(sub, FirestoreDocuments.ToSubCategory("s", subDocument));
    }

    // PRD 7.4 plans/{fyId}_{subCategoryId}: amounts by month key in long minor units.
    [Fact]
    public void Plan_keeps_minor_units_by_month_key()
    {
        var plan = new PlanAmounts("s", "c", new Dictionary<string, long> { ["2026-01"] = 150_050 });

        var document = FirestoreDocuments.ToDocument("2026-01", plan);

        Assert.Equal(("2026-01", "s", "c", 150_050L), (document.FyId, document.SubCategoryId, document.CategoryId, document.Amounts["2026-01"]));
        var back = FirestoreDocuments.ToPlan(document);
        Assert.Equal((plan.SubCategoryId, plan.CategoryId), (back.SubCategoryId, back.CategoryId));
        Assert.Equal(plan.Amounts, back.Amounts);
    }

    [Fact]
    public void Income_comes_back_unchanged()
    {
        var salary = new IncomeItem("a", IncomeKind.Salary, "Salary", 1_500_050, "2026-01", null);
        var once = new IncomeItem("b", IncomeKind.Additional, "Bonus", 50_025, "2026-09", "2026-09");

        Assert.Equal(salary, FirestoreDocuments.ToIncome("a", FirestoreDocuments.ToDocument(salary)));
        Assert.Equal(once, FirestoreDocuments.ToIncome("b", FirestoreDocuments.ToDocument(once)));
    }

    // R-INC-04, R-INC-05 (owner note 17): what an item counts in, and the account it goes into.
    [Fact]
    public void Income_counts_come_back_unchanged()
    {
        var planned = new IncomeItem("a", IncomeKind.Additional, "Bonus", 300_000, "2026-12", "2026-12", IncomeCounts.Planned);
        var actual = new IncomeItem("b", IncomeKind.Additional, "Gift", 50_000, "2026-09", "2026-09", IncomeCounts.Actual, "main");

        Assert.Equal(("actual", "main"), (FirestoreDocuments.ToDocument(actual).Counts, FirestoreDocuments.ToDocument(actual).BankAccountId));
        Assert.Equal(planned, FirestoreDocuments.ToIncome("a", FirestoreDocuments.ToDocument(planned)));
        Assert.Equal(actual, FirestoreDocuments.ToIncome("b", FirestoreDocuments.ToDocument(actual)));
    }

    // R-INC-04: an item saved before owner note 17 has no counts and is both.
    [Fact]
    public void Income_without_counts_is_both()
    {
        var document = new IncomeDocument { Kind = "additional", Label = "Rent", AmountMinor = 200_000, FromPeriodKey = "2026-01" };

        Assert.Equal(IncomeCounts.Both, FirestoreDocuments.ToIncome("a", document).Counts);
        Assert.Null(FirestoreDocuments.ToIncome("a", document).BankAccountId);
    }

    // PRD 7.4 entries: YYYY-MM-DD date, long minor units, periodKey and fyId set by the API.
    [Fact]
    public void Entry_comes_back_unchanged()
    {
        var entry = new Entry("e", new DateOnly(2026, 10, 17), "2026-09", "2026-01", "food", "groceries", "bank", 12_550, "Weekly shop");

        var document = FirestoreDocuments.ToDocument(entry);

        Assert.Equal(("2026-10-17", "2026-09", "2026-01", "food", "groceries", "bank", 12_550L, "Weekly shop"),
            (document.Date, document.PeriodKey, document.FyId, document.CategoryId, document.SubCategoryId,
             document.BankAccountId, document.AmountMinor, document.Note));
        Assert.Equal(entry, FirestoreDocuments.ToEntry("e", document));
    }

    [Fact]
    public void Bank_account_comes_back_unchanged()
    {
        var account = new BankAccount("a", "Al Rajhi", Archived: true);

        Assert.Equal(account, FirestoreDocuments.ToBankAccount("a", FirestoreDocuments.ToDocument(account)));
    }

    // AUTH-06: createdAt is set by the server, so the round trip puts it in by hand.
    [Fact]
    public void Invite_comes_back_unchanged()
    {
        var created = new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);
        var invite = new Invite("hash", "uid-1", "owner@example.com", created, created.AddDays(7));

        var document = AccessDocuments.ToDocument(invite);
        document.CreatedAt = Google.Cloud.Firestore.Timestamp.FromDateTimeOffset(created);

        Assert.Equal(("uid-1", "owner@example.com"), (document.CreatedByUid, document.CreatedByEmail));
        Assert.Equal(invite, AccessDocuments.ToInvite("hash", document));
    }

    [Fact]
    public void Member_comes_back_unchanged()
    {
        var member = new Member("m", "uid-2", "friend@example.com", new DateTimeOffset(2026, 10, 19, 6, 0, 0, TimeSpan.Zero), "owner@example.com");

        var document = AccessDocuments.ToDocument(member);

        Assert.Equal(("uid-2", "friend@example.com", "owner@example.com"), (document.Uid, document.Email, document.InvitedByEmail));
        Assert.Equal(member, AccessDocuments.ToMember("m", document));
    }
}
