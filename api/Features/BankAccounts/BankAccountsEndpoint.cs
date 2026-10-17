using System.Security.Claims;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Features.BankAccounts;

// PRD 7.5 GET /api/bank-accounts, for the Add form (PRD 5.4). Add, rename and archive come with
// Settings (M8, STG-04). There is never a DELETE (PRD 4.6).
public static class BankAccountsEndpoint
{
    public record BankAccountDto(string Id, string Name, bool Archived);

    public static IEndpointRouteBuilder MapBankAccounts(this IEndpointRouteBuilder api)
    {
        // Sorted by name ignoring case; the first active one is the default before any entry (M5).
        // Archived ones are included and flagged.
        api.MapGet("/bank-accounts", async (ClaimsPrincipal user, IUserStore store) =>
            Sorted(await store.GetBankAccountsAsync(user.FindFirstValue("sub")!)));
        return api;
    }

    // Also part of GET /api/entries (M6).
    public static IReadOnlyList<BankAccountDto> Sorted(IEnumerable<BankAccount> accounts) => accounts
        .OrderBy(account => Names.Lower(account.Name), StringComparer.Ordinal)
        .Select(account => new BankAccountDto(account.Id, account.Name, account.Archived))
        .ToList();
}
