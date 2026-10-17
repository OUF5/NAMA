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

public sealed record Category(string Id, string Name, int SortOrder, bool Archived = false);

// PRD 7.4: belongs to exactly one category and cannot move to another in Phase 1.
public sealed record SubCategory(string Id, string CategoryId, string Name, int SortOrder, bool Archived = false);

public static class DocumentIds
{
    // Ids are GUIDs (32 hex characters) made by the API (M3).
    public static string New() => Guid.NewGuid().ToString("N");
}

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

    Task<IReadOnlyList<Category>> GetCategoriesAsync(string uid);

    Task<IReadOnlyList<SubCategory>> GetSubCategoriesAsync(string uid);

    // Names are checked by the caller (PRD 4.7). There is no delete (PRD 4.6).
    Task AddCategoryAsync(string uid, Category category);

    Task AddSubCategoryAsync(string uid, SubCategory subCategory);

    Task<IReadOnlyList<PlanAmounts>> GetPlansAsync(string uid, string fyId);

    // Replaces the month amounts of one sub-category in one FY.
    Task SetPlanAsync(string uid, string fyId, PlanAmounts plan);

    Task<IReadOnlyList<IncomeItem>> GetIncomesAsync(string uid);
}
