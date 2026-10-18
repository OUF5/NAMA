using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.Categories;
using Api.Infrastructure;

namespace Api.Features.BankAccounts;

// PRD 7.5 bank accounts: list for the Add form (PRD 5.4); add, rename, archive and restore in
// Settings (STG-04). There is never a DELETE (PRD 4.6).
public static class BankAccountsEndpoint
{
    public record BankAccountDto(string Id, string Name, bool Archived);

    public static IEndpointRouteBuilder MapBankAccounts(this IEndpointRouteBuilder api)
    {
        // Sorted by name ignoring case; the first active one is the default before any entry (M5).
        // Archived ones are included and flagged.
        api.MapGet("/bank-accounts", async (ClaimsPrincipal user, IUserStore store) =>
            Sorted(await store.GetBankAccountsAsync(user.FindFirstValue("sub")!)));

        // PRD 4.7: unique ignoring case per user, archived ones included.
        api.MapPost("/bank-accounts", async (BankAccountRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if ((await store.GetBankAccountsAsync(uid)).Any(account => Names.Lower(account.Name) == Names.Lower(request.Name)))
            {
                return Invalid(nameof(BankAccountRequest.Name), "A bank account with this name already exists.");
            }
            var account = new BankAccount(DocumentIds.New(), Names.Normalize(request.Name));
            await store.AddBankAccountAsync(uid, account);
            return Results.Created($"/api/bank-accounts/{account.Id}", ToDto(account));
        });

        // Rename, archive and restore. Entries need an account, so the last active one stays active.
        api.MapPut("/bank-accounts/{id}", async (string id, UpdateItemRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var accounts = await store.GetBankAccountsAsync(uid);
            if (accounts.FirstOrDefault(account => account.Id == id) is not { } current)
            {
                return Results.NotFound();
            }
            if (accounts.Any(account => account.Id != id && Names.Lower(account.Name) == Names.Lower(request.Name)))
            {
                return Invalid(nameof(UpdateItemRequest.Name), "A bank account with this name already exists.");
            }
            if (request.Archived == true && !accounts.Any(account => account.Id != id && !account.Archived))
            {
                return Invalid(nameof(UpdateItemRequest.Archived), "Keep at least one active bank account.");
            }
            var account = current with { Name = Names.Normalize(request.Name), Archived = request.Archived!.Value };
            return await store.UpdateBankAccountAsync(uid, account) ? Results.Ok(ToDto(account)) : Results.NotFound();
        });

        return api;
    }

    // Also part of GET /api/entries (M6).
    public static IReadOnlyList<BankAccountDto> Sorted(IEnumerable<BankAccount> accounts) => accounts
        .OrderBy(account => Names.Lower(account.Name), StringComparer.Ordinal)
        .Select(ToDto)
        .ToList();

    private static BankAccountDto ToDto(BankAccount account) => new(account.Id, account.Name, account.Archived);

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class BankAccountRequest : IValidatableObject
{
    public string? Name { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Names.IsValid(Name))
        {
            yield return new ValidationResult("Name is 1 to 40 characters.", [nameof(Name)]);
        }
    }
}
