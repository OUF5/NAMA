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

public sealed record BankAccount(string Id, string Name, bool Archived = false);

// PRD 7.4 entry. PeriodKey and FyId are set by the API from Date (R-CAL-09); CategoryId is
// copied from the sub-category for filtering.
public sealed record Entry(
    string Id,
    DateOnly Date,
    string PeriodKey,
    string FyId,
    string CategoryId,
    string SubCategoryId,
    string BankAccountId,
    long AmountMinor,
    string Note);

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

    Task<IReadOnlyList<BankAccount>> GetBankAccountsAsync(string uid);

    // Creates the entry and sets the profile's lastBankAccountId in one batch (decision 5).
    Task AddEntryAsync(string uid, Entry entry);

    // The entries of one sub-category in one month (ENT-01 "spent so far").
    Task<IReadOnlyList<Entry>> GetEntriesAsync(string uid, string subCategoryId, string periodKey);

    // Null when the entry does not exist.
    Task<Entry?> GetEntryAsync(string uid, string id);

    // All entries of one FY, newest first (TBL-01): date descending, then the one added later first.
    Task<IReadOnlyList<Entry>> GetFiscalYearEntriesAsync(string uid, string fyId);

    // ENT-04: every field except createdAt. lastBankAccountId is not changed (M6 owner's choice).
    // False when the entry no longer exists.
    Task<bool> UpdateEntryAsync(string uid, Entry entry);

    // TBL-03. False when the entry no longer exists.
    Task<bool> DeleteEntryAsync(string uid, string id);
}
