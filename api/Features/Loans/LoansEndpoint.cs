using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.Banks;
using Api.Features.Entries;
using Api.Features.Overview;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.Loans;

// PRD 5.12 Loans (owner note 20): money lent and borrowed, kept for information only (R-LON-02). No
// other endpoint reads a loan, so a loan never changes a balance, income, a plan or Tracker. Every
// change answers like GET /api/loans, so the page needs no second call (NFR-01).
public static class LoansEndpoint
{
    public record LoanDto(string Id, string Name, string Type, decimal Amount, DateOnly Date, string BankAccountId, string Note, DateOnly? SettledOn)
    {
        public static LoanDto From(Loan loan) => new(loan.Id, loan.Name, FirestoreDocuments.ToLoanTypeName(loan.Type),
            Money.FromMinor(loan.AmountMinor), loan.Date, loan.BankAccountId, loan.Note, loan.SettledOn);
    }

    public record TypeTotalsDto(decimal Open, int OpenCount, decimal Settled, int SettledCount)
    {
        public static TypeTotalsDto From(LoanTypeTotals totals) =>
            new(Money.FromMinor(totals.Open), totals.OpenCount, Money.FromMinor(totals.Settled), totals.SettledCount);
    }

    // R-LON-04. Net is Lent − Borrowed of the open loans. No names (Dashboard's card, DSH-07).
    public record TotalsDto(TypeTotalsDto Lent, TypeTotalsDto Borrowed, decimal Net)
    {
        public static TotalsDto From(LoanTotals totals) =>
            new(TypeTotalsDto.From(totals.Lent), TypeTotalsDto.From(totals.Borrowed), Money.FromMinor(totals.Net));
    }

    public record PersonDto(string Name, decimal LentOpen, decimal BorrowedOpen, int OpenCount);

    // Every account by name, archived ones too (an old loan can name one). Balance is today's (R-BAL-03),
    // null without an opening balance.
    public record AccountDto(string Id, string Name, bool Archived, decimal? Balance);

    // LON-02: the current FY's month ends, real or projected (as Tracker's Balance row), for what NAMA
    // knows about the date picked. Empty when no FY contains today.
    public record MonthDto(MeEndpoint.PeriodDto Period, decimal? Balance, bool Projected);

    public record LoansResponse(
        DateOnly Today,
        IReadOnlyList<LoanDto> Loans,
        TotalsDto Totals,
        IReadOnlyList<PersonDto> People,
        IReadOnlyList<AccountDto> Accounts,
        string? LastBankAccountId,
        bool Complete,
        decimal? Balance,
        IReadOnlyList<MonthDto> Months);

    public static IEndpointRouteBuilder MapLoans(this IEndpointRouteBuilder api)
    {
        api.MapGet("/loans", (ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
            ResultAsync(user.FindFirstValue("sub")!, store, time));

        // DSH-07: only the totals, so Dashboard reads no names and no balances.
        api.MapGet("/loans/totals", async (ClaimsPrincipal user, IUserStore store) =>
            TotalsDto.From(LoanCalculator.Totals(await store.GetLoansAsync(user.FindFirstValue("sub")!))));

        api.MapPost("/loans", async (LoanRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var (loan, error) = await BuildAsync(uid, DocumentIds.New(), request, current: null, store);
            if (loan is null)
            {
                return error!;
            }
            await store.AddLoanAsync(uid, loan);
            return await ResultAsync(uid, store, time, created: true);
        });

        // An unknown loan, or another user's, is 404. The settled date stays (R-LON-03).
        api.MapPut("/loans/{id}", async (string id, LoanRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (await store.GetLoanAsync(uid, id) is not { } current)
            {
                return Results.NotFound();
            }
            var (loan, error) = await BuildAsync(uid, id, request, current, store);
            if (loan is null)
            {
                return error!;
            }
            return await store.UpdateLoanAsync(uid, loan) ? await ResultAsync(uid, store, time) : Results.NotFound();
        });

        // R-LON-03: returned (Lent) or paid (Borrowed) in full, or open again with null.
        api.MapPut("/loans/{id}/settled", async (string id, SettledRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (await store.GetLoanAsync(uid, id) is not { } loan)
            {
                return Results.NotFound();
            }
            if (request.SettledOn is { } date && !LoanCalculator.CanSettleOn(loan, date))
            {
                return Invalid(nameof(SettledRequest.SettledOn), "The date is on or after the loan's date.");
            }
            return await store.UpdateLoanAsync(uid, loan with { SettledOn = request.SettledOn })
                ? await ResultAsync(uid, store, time)
                : Results.NotFound();
        });

        // The confirmation is in the web app (UX-04).
        api.MapDelete("/loans/{id}", async (string id, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            return await store.DeleteLoanAsync(uid, id) ? await ResultAsync(uid, store, time) : Results.NotFound();
        });

        return api;
    }

    // LON-02, LON-03: the loans and what NAMA knows about the balances, read as Tracker's Balance row
    // reads them (BalancesEndpoint), from the FY that contains today.
    public static async Task<IResult> ResultAsync(string uid, IUserStore store, TimeProvider time, bool created = false)
    {
        if (await LoadAsync(uid, store, time) is not { } response)
        {
            return Results.NotFound();
        }
        return created ? Results.Created("/api/loans", response) : Results.Ok(response);
    }

    // Null when the user has no profile. Also the Claude connector's get_loans (MCP-04).
    public static async Task<LoansResponse?> LoadAsync(string uid, IUserStore store, TimeProvider time)
    {
        var profile = await store.GetProfileAsync(uid);
        if (profile is null)
        {
            return null;
        }
        var fiscalYear = FiscalCalendar.FindPeriod(await store.GetFiscalYearsAsync(uid), FiscalCalendar.Today(time, profile.Timezone))?.FiscalYear;
        if (await BanksEndpoint.LoadAsync(uid, store, time, fiscalYear?.StartDate) is not { } data)
        {
            return null;
        }
        var loans = await store.GetLoansAsync(uid);
        var months = fiscalYear is null ? [] : await BalancesEndpoint.MonthEndsAsync(uid, store, data, fiscalYear);
        return new LoansResponse(
            data.Today,
            loans.Select(LoanDto.From).ToList(),
            TotalsDto.From(LoanCalculator.Totals(loans)),
            LoanCalculator.ByPerson(loans)
                .Select(person => new PersonDto(person.Name, Money.FromMinor(person.LentOpen), Money.FromMinor(person.BorrowedOpen), person.OpenCount))
                .ToList(),
            data.Accounts
                .OrderBy(account => account.Name, StringComparer.OrdinalIgnoreCase)
                .Select(account => new AccountDto(account.Id, account.Name, account.Archived,
                    data.Balances.TryGetValue(account.Id, out var balance) ? Money.FromMinor(balance.Balance) : null))
                .ToList(),
            data.Profile.LastBankAccountId,
            data.Complete,
            data.Complete && data.Total is { } total ? Money.FromMinor(total) : null,
            months.Select(end => new MonthDto(MeEndpoint.PeriodDto.From(end.Period),
                end.Balance is { } amount ? Money.FromMinor(amount) : null, end.Projected)).ToList());
    }

    // R-LON-01: pickers hide archived accounts, so the API refuses them too, except the one an edited
    // loan already names. Any date counts (owner's rule), but an edited settled loan cannot move after
    // its settled date (R-LON-03).
    private static async Task<(Loan? Loan, IResult? Error)> BuildAsync(string uid, string id, LoanRequest request, Loan? current, IUserStore store)
    {
        var accounts = await store.GetBankAccountsAsync(uid);
        if (!accounts.Any(account => account.Id == request.BankAccountId && (!account.Archived || account.Id == current?.BankAccountId)))
        {
            return (null, Invalid(nameof(LoanRequest.BankAccountId), "Bank account is unknown."));
        }
        var loan = new Loan(
            id,
            Names.Normalize(request.Name),
            FirestoreDocuments.ToLoanType(request.Type),
            Money.ToMinor(request.Amount!.Value),
            request.Date!.Value,
            request.BankAccountId!,
            (request.Note ?? "").Trim(),
            current?.SettledOn);
        if (loan.SettledOn is { } settled && !LoanCalculator.CanSettleOn(loan, settled))
        {
            return (null, Invalid(nameof(LoanRequest.Date), "The date is on or before the day it was settled."));
        }
        return (loan, null);
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

// R-LON-01: Type is "lent" or "borrowed".
public sealed class LoanRequest : IValidatableObject
{
    public string? Name { get; init; }

    [Required]
    [AllowedValues("lent", "borrowed")]
    public string? Type { get; init; }

    [Required]
    public decimal? Amount { get; init; }

    [Required]
    public DateOnly? Date { get; init; }

    [Required]
    public string? BankAccountId { get; init; }

    public string? Note { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Names.IsValid(Name))
        {
            yield return new ValidationResult("Name is 1 to 40 characters.", [nameof(Name)]);
        }
        // R-MON-02
        if (Amount is { } amount && !Money.IsValidAmount(amount))
        {
            yield return new ValidationResult("Amount is more than 0 and at most 10,000,000, with at most 2 decimals.", [nameof(Amount)]);
        }
        // PRD 4.7: notes are optional, at most 200 characters.
        if ((Note ?? "").Trim().Length > EntryRequest.NoteMaxLength)
        {
            yield return new ValidationResult("Note is at most 200 characters.", [nameof(Note)]);
        }
    }
}

// R-LON-03: null opens the loan again.
public sealed class SettledRequest
{
    public DateOnly? SettledOn { get; init; }
}
