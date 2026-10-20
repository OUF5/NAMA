using Api.Domain;

namespace Api.Infrastructure;

// PRD 7.4 users/{uid}. FiscalStartMonth and PaydayDay are used for the next FY created (R-CAL-07).
// StartedOn is the day setup was finished (the profile's createdAt in its time zone, R-PLN-06); it is
// read only, and null when unknown.
public sealed record UserProfile(
    string DisplayName,
    string Email,
    string ProjectName,
    string Currency,
    string Timezone,
    int FiscalStartMonth,
    int PaydayDay,
    bool SetupCompleted,
    string? LastBankAccountId,
    DateOnly? StartedOn = null);

// R-BAL-01: Opening is null until it is set (accounts made before 20 Oct 2026 have none).
public sealed record BankAccount(string Id, string Name, bool Archived = false, OpeningBalance? Opening = null);

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

// R-TRF-01: money moved between two of the user's own accounts. Never spending, never income.
// PeriodKey and FyId are set by the API from Date (R-CAL-09).
public sealed record Transfer(
    string Id,
    DateOnly Date,
    string PeriodKey,
    string FyId,
    string FromBankAccountId,
    string ToBankAccountId,
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
    IReadOnlyList<Category> Categories,
    IncomeSplit IncomeSplit);

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

    // The entries of one sub-category in one FY (ENT-01 "spent so far", with what carried in, R-PLN-05).
    Task<IReadOnlyList<Entry>> GetSubCategoryEntriesAsync(string uid, string subCategoryId, string fyId);

    // Every entry dated on or after a day, in any FY (R-BAL-03 balances, the top bar).
    Task<IReadOnlyList<Entry>> GetEntriesFromAsync(string uid, DateOnly from);

    // Null when the entry does not exist.
    Task<Entry?> GetEntryAsync(string uid, string id);

    // All entries of one FY, newest first (TBL-01): date descending, then the one added later first.
    Task<IReadOnlyList<Entry>> GetFiscalYearEntriesAsync(string uid, string fyId);

    // ENT-04: every field except createdAt. lastBankAccountId is not changed (M6 owner's choice).
    // False when the entry no longer exists.
    Task<bool> UpdateEntryAsync(string uid, Entry entry);

    // TBL-03. False when the entry no longer exists.
    Task<bool> DeleteEntryAsync(string uid, string id);

    // STG-01. The other profile fields are not changed.
    Task UpdateProfileAsync(string uid, string displayName, string projectName, string currency, string timezone);

    // STG-02, R-CAL-07: used for the next FY created.
    Task UpdateCalendarAsync(string uid, int fiscalStartMonth, int paydayDay);

    // FY-02: the FY and its copied plans in one batch. False when the FY already exists (409).
    Task<bool> StartFiscalYearAsync(string uid, FiscalYear fiscalYear, IReadOnlyList<PlanAmounts> plans);

    // R-INC-02 in one batch: the salary items that now end earlier, the ones replaced, and the new one.
    Task ChangeSalaryAsync(string uid, IReadOnlyList<IncomeItem> ended, IReadOnlyList<string> replacedIds, IncomeItem created);

    // R-INC-03. Update and delete return false when the item no longer exists.
    Task AddIncomeAsync(string uid, IncomeItem income);

    Task<bool> UpdateIncomeAsync(string uid, IncomeItem income);

    Task<bool> DeleteIncomeAsync(string uid, string id);

    // STG-04, STG-05: add, rename, archive. Names are checked by the caller (PRD 4.7). Updates
    // return false when the item no longer exists. There is no delete (PRD 4.6).
    Task AddBankAccountAsync(string uid, BankAccount account);

    // Name, archived and the opening balance (R-BAL-01).
    Task<bool> UpdateBankAccountAsync(string uid, BankAccount account);

    // R-BAL-02: one document per month a split starts in. Setting a split replaces the ones from its
    // month on (replacedFromKeys, its own month included) in one batch.
    Task<IReadOnlyList<IncomeSplit>> GetIncomeSplitsAsync(string uid);

    Task SetIncomeSplitAsync(string uid, IncomeSplit split, IReadOnlyList<string> replacedFromKeys);

    // R-TRF-01. Newest first: date descending, then the one added later first. Update and delete
    // return false when the transfer no longer exists.
    Task<IReadOnlyList<Transfer>> GetTransfersAsync(string uid);

    Task<Transfer?> GetTransferAsync(string uid, string id);

    Task AddTransferAsync(string uid, Transfer transfer);

    Task<bool> UpdateTransferAsync(string uid, Transfer transfer);

    Task<bool> DeleteTransferAsync(string uid, string id);

    Task<bool> UpdateCategoryAsync(string uid, Category category);

    Task<bool> UpdateSubCategoryAsync(string uid, SubCategory subCategory);
}
