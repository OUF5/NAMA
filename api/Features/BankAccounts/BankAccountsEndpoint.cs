using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.Banks;
using Api.Infrastructure;

namespace Api.Features.BankAccounts;

// PRD 7.5 bank accounts: the list for the Add form (PRD 5.4); add, rename, set the opening balance,
// archive and restore on Banks (BNK-02). There is never a DELETE (PRD 4.6). Changes answer with
// GET /api/banks, because they move the balances.
public static class BankAccountsEndpoint
{
    public record BankAccountDto(string Id, string Name, bool Archived);

    public static IEndpointRouteBuilder MapBankAccounts(this IEndpointRouteBuilder api)
    {
        // Sorted by name ignoring case; the first active one is the default before any entry (M5).
        // Archived ones are included and flagged.
        api.MapGet("/bank-accounts", async (ClaimsPrincipal user, IUserStore store) =>
            Sorted(await store.GetBankAccountsAsync(user.FindFirstValue("sub")!)));

        // PRD 4.7: unique ignoring case per user, archived ones included. R-BAL-01: a new account
        // starts with its opening balance.
        api.MapPost("/bank-accounts", async (BankAccountRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if ((await store.GetBankAccountsAsync(uid)).Any(account => Names.Lower(account.Name) == Names.Lower(request.Name)))
            {
                return Invalid(nameof(BankAccountRequest.Name), "A bank account with this name already exists.");
            }
            if (request.Opening() is null)
            {
                return Invalid(nameof(BankAccountRequest.OpeningBalance), "Enter the balance and its date.");
            }
            if (await OpeningError(uid, request, store) is { } error)
            {
                return error;
            }
            await store.AddBankAccountAsync(uid, new BankAccount(DocumentIds.New(), Names.Normalize(request.Name), Opening: request.Opening()));
            return await BanksEndpoint.ResultAsync(uid, store, time, created: true);
        });

        // Rename, set the opening balance, archive and restore. Entries need an account, so the last
        // active one stays active, and an account that gets income from now on (the latest split,
        // R-BAL-02) cannot be archived.
        api.MapPut("/bank-accounts/{id}", async (string id, BankAccountRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var accounts = await store.GetBankAccountsAsync(uid);
            if (accounts.FirstOrDefault(account => account.Id == id) is not { } current)
            {
                return Results.NotFound();
            }
            if (accounts.Any(account => account.Id != id && Names.Lower(account.Name) == Names.Lower(request.Name)))
            {
                return Invalid(nameof(BankAccountRequest.Name), "A bank account with this name already exists.");
            }
            if (request.Archived is null)
            {
                return Invalid(nameof(BankAccountRequest.Archived), "Archived is required.");
            }
            if (request.Archived == true && !current.Archived)
            {
                if (!accounts.Any(account => account.Id != id && !account.Archived))
                {
                    return Invalid(nameof(BankAccountRequest.Archived), "Keep at least one active bank account.");
                }
                var latest = (await store.GetIncomeSplitsAsync(uid)).MaxBy(split => split.FromPeriodKey, StringComparer.Ordinal);
                if (latest?.Shares.GetValueOrDefault(id) > 0)
                {
                    return Invalid(nameof(BankAccountRequest.Archived), "This account gets a share of income. Give its share to another account first.");
                }
            }
            if (await OpeningError(uid, request, store) is { } error)
            {
                return error;
            }
            var account = current with { Name = Names.Normalize(request.Name), Archived = request.Archived.Value, Opening = request.Opening() };
            return await store.UpdateBankAccountAsync(uid, account) ? await BanksEndpoint.ResultAsync(uid, store, time) : Results.NotFound();
        });

        return api;
    }

    // Also part of GET /api/entries (M6).
    public static IReadOnlyList<BankAccountDto> Sorted(IEnumerable<BankAccount> accounts) => accounts
        .OrderBy(account => Names.Lower(account.Name), StringComparer.Ordinal)
        .Select(ToDto)
        .ToList();

    private static BankAccountDto ToDto(BankAccount account) => new(account.Id, account.Name, account.Archived);

    // R-BAL-01: the opening date is a day of a stored FY, like every date in NAMA (R-STA-03).
    private static async Task<IResult?> OpeningError(string uid, BankAccountRequest request, IUserStore store)
    {
        if (request.Opening() is { } opening && FiscalCalendar.FindPeriod(await store.GetFiscalYearsAsync(uid), opening.Date) is null)
        {
            return Invalid(nameof(BankAccountRequest.OpeningDate), "Pick a date inside a fiscal year.");
        }
        return null;
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

// R-BAL-01: the opening balance and its date come together, or not at all (an account made before
// balances existed). Archived is for PUT only.
public sealed class BankAccountRequest : IValidatableObject
{
    public string? Name { get; init; }

    public bool? Archived { get; init; }

    public decimal? OpeningBalance { get; init; }

    public DateOnly? OpeningDate { get; init; }

    public OpeningBalance? Opening() =>
        OpeningBalance is { } amount && OpeningDate is { } date ? new OpeningBalance(Money.ToMinor(amount), date) : null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Names.IsValid(Name))
        {
            yield return new ValidationResult("Name is 1 to 40 characters.", [nameof(Name)]);
        }
        if (OpeningBalance is { } amount && !Money.IsValidBalance(amount))
        {
            yield return new ValidationResult("Balance is -10,000,000 to 10,000,000 with at most 2 decimals.", [nameof(OpeningBalance)]);
        }
        if ((OpeningBalance is null) != (OpeningDate is null))
        {
            yield return new ValidationResult("Enter the balance and its date.", [OpeningDate is null ? nameof(OpeningDate) : nameof(OpeningBalance)]);
        }
    }
}
