using Api.Domain;

namespace Api.Infrastructure;

// PRD 7.4 users/{uid}. FiscalStartMonth and PaydayDay are used for the next FY created (R-CAL-07).
public sealed record UserProfile(
    string DisplayName,
    string Email,
    string ProjectName,
    string Currency,
    string Timezone,
    int FiscalStartMonth,
    int PaydayDay,
    bool SetupCompleted,
    string? LastBankAccountId);

public sealed record BankAccount(string Id, string Name);

public sealed record Category(string Id, string Name, int SortOrder);

// Everything the wizard saves in one batch (SET-02).
public sealed record SetupData(
    UserProfile Profile,
    FiscalYear FiscalYear,
    IReadOnlyList<IncomeItem> Incomes,
    IReadOnlyList<BankAccount> BankAccounts,
    IReadOnlyList<Category> Categories);

// All data of one user (NFR-03). The uid always comes from the token's "sub" claim (AUTH-03).
public interface IUserStore
{
    Task<UserProfile?> GetProfileAsync(string uid);

    Task<IReadOnlyList<FiscalYear>> GetFiscalYearsAsync(string uid);

    // False when the user is already set up (409).
    Task<bool> CreateSetupAsync(string uid, SetupData setup);
}
