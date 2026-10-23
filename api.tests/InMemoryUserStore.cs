using System.Collections.Concurrent;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Tests;

// Stands in for Firestore in endpoint tests (CI has no emulator). The Firestore store is
// checked by hand against the emulator.
public sealed class InMemoryUserStore : IUserStore
{
    private sealed class UserData(SetupData setup)
    {
        public SetupData Setup { get; } = setup;
        // lastBankAccountId changes with every entry (decision 5).
        public UserProfile Profile { get; set; } = setup.Profile;
        public List<FiscalYear> FiscalYears { get; } = [setup.FiscalYear];
        public List<IncomeItem> Incomes { get; } = [.. setup.Incomes];
        public List<BankAccount> BankAccounts { get; } = [.. setup.BankAccounts];
        public List<Entry> Entries { get; } = [];
        public List<Category> Categories { get; } = [.. setup.Categories];
        public List<SubCategory> SubCategories { get; } = [];
        // Key: {fyId}_{subCategoryId}, as in Firestore (PRD 7.4).
        public Dictionary<string, (string FyId, PlanAmounts Plan)> Plans { get; } = [];
        // Key: fromPeriodKey, as in Firestore (R-BAL-02).
        public Dictionary<string, IncomeSplit> IncomeSplits { get; } = new() { [setup.IncomeSplit.FromPeriodKey] = setup.IncomeSplit };
        // In the order added, which stands in for createdAt.
        public List<Transfer> Transfers { get; } = [];
        public List<Loan> Loans { get; } = [];
    }

    private readonly ConcurrentDictionary<string, UserData> _users = new();

    // What the wizard saved, unchanged by later adds.
    public SetupData? Saved(string uid) => _users.GetValueOrDefault(uid)?.Setup;

    public Task<UserProfile?> GetProfileAsync(string uid) =>
        Task.FromResult(_users.TryGetValue(uid, out var data) ? data.Profile : null);

    // Every entry of a user, as stored.
    public IReadOnlyList<Entry> Entries(string uid) => Read<Entry>(uid, data => data.Entries).Result;

    // Adds an FY without plans, for tests that only need its months.
    public Task AddFiscalYear(string uid, FiscalYear fiscalYear) => Write(uid, data => data.FiscalYears.Add(fiscalYear));

    public Task<IReadOnlyList<FiscalYear>> GetStoredFiscalYearsAsync(string uid) => Read<FiscalYear>(uid, data => data.FiscalYears);

    public Task<bool> CreateSetupAsync(string uid, SetupData setup) => Task.FromResult(_users.TryAdd(uid, new UserData(setup)));

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(string uid) =>
        Read<Category>(uid, data => data.Categories);

    public Task<IReadOnlyList<SubCategory>> GetSubCategoriesAsync(string uid) =>
        Read<SubCategory>(uid, data => data.SubCategories);

    public Task AddCategoryAsync(string uid, Category category) => Write(uid, data => data.Categories.Add(category));

    public Task AddSubCategoryAsync(string uid, SubCategory subCategory) => Write(uid, data => data.SubCategories.Add(subCategory));

    public Task<IReadOnlyList<PlanAmounts>> GetPlansAsync(string uid, string fyId) =>
        Read<PlanAmounts>(uid, data => data.Plans.Values.Where(entry => entry.FyId == fyId).Select(entry => entry.Plan));

    public Task SetPlanAsync(string uid, string fyId, PlanAmounts plan) =>
        Write(uid, data => data.Plans[$"{fyId}_{plan.SubCategoryId}"] = (fyId, plan));

    public Task<IReadOnlyList<IncomeItem>> GetIncomesAsync(string uid) =>
        Read<IncomeItem>(uid, data => data.Incomes);

    public Task<IReadOnlyList<BankAccount>> GetBankAccountsAsync(string uid) =>
        Read<BankAccount>(uid, data => data.BankAccounts);

    public Task AddEntryAsync(string uid, Entry entry) => Write(uid, data =>
    {
        data.Entries.Add(entry);
        data.Profile = data.Profile with { LastBankAccountId = entry.BankAccountId };
    });

    public Task<IReadOnlyList<Entry>> GetSubCategoryEntriesAsync(string uid, string subCategoryId, string fyId) =>
        Read<Entry>(uid, data => data.Entries.Where(entry => entry.SubCategoryId == subCategoryId && entry.FyId == fyId));

    // How many times balances read the entries (the top bar's cost, R-BAL-03).
    public int EntriesFromReads { get; private set; }

    public Task<IReadOnlyList<Entry>> GetEntriesFromAsync(string uid, DateOnly from)
    {
        EntriesFromReads++;
        return Read<Entry>(uid, data => data.Entries.Where(entry => entry.Date >= from));
    }

    public async Task<Entry?> GetEntryAsync(string uid, string id) =>
        (await Read<Entry>(uid, data => data.Entries.Where(entry => entry.Id == id))).SingleOrDefault();

    // Newest first; Entries keeps the order they were added in, which stands in for createdAt.
    public Task<IReadOnlyList<Entry>> GetFiscalYearEntriesAsync(string uid, string fyId) =>
        Read<Entry>(uid, data => data.Entries
            .Select((entry, added) => (entry, added))
            .Where(item => item.entry.FyId == fyId)
            .OrderByDescending(item => item.entry.Date)
            .ThenByDescending(item => item.added)
            .Select(item => item.entry));

    // The entry keeps its place in Entries, as createdAt is kept in Firestore.
    public Task<bool> UpdateEntryAsync(string uid, Entry entry) => Change(uid, data =>
    {
        var index = data.Entries.FindIndex(stored => stored.Id == entry.Id);
        if (index < 0)
        {
            return false;
        }
        data.Entries[index] = entry;
        return true;
    });

    public Task<bool> DeleteEntryAsync(string uid, string id) =>
        Change(uid, data => data.Entries.RemoveAll(entry => entry.Id == id) > 0);

    public Task UpdateProfileAsync(string uid, string displayName, string projectName, string currency, string timezone) =>
        Write(uid, data => data.Profile = data.Profile with
        {
            DisplayName = displayName,
            ProjectName = projectName,
            Currency = currency,
            Timezone = timezone,
        });

    public Task UpdateCalendarAsync(string uid, int fiscalStartMonth, int paydayDay, MonthNaming monthNaming) =>
        Write(uid, data => data.Profile = data.Profile with
        {
            FiscalStartMonth = fiscalStartMonth, PaydayDay = paydayDay, MonthNaming = monthNaming,
        });

    // As the Firestore batch: nothing is written when the FY already exists.
    public Task<bool> StartFiscalYearAsync(string uid, FiscalYear fiscalYear, IReadOnlyList<PlanAmounts> plans) => Change(uid, data =>
    {
        if (data.FiscalYears.Any(stored => stored.Id == fiscalYear.Id))
        {
            return false;
        }
        data.FiscalYears.Add(fiscalYear);
        foreach (var plan in plans)
        {
            data.Plans[$"{fiscalYear.Id}_{plan.SubCategoryId}"] = (fiscalYear.Id, plan);
        }
        return true;
    });

    public Task ChangeSalaryAsync(string uid, IReadOnlyList<IncomeItem> ended, IReadOnlyList<string> replacedIds, IncomeItem created) =>
        Write(uid, data =>
        {
            foreach (var item in ended)
            {
                var index = data.Incomes.FindIndex(stored => stored.Id == item.Id);
                data.Incomes[index] = data.Incomes[index] with { ToPeriodKey = item.ToPeriodKey };
            }
            data.Incomes.RemoveAll(stored => replacedIds.Contains(stored.Id));
            data.Incomes.Add(created);
        });

    public Task AddIncomeAsync(string uid, IncomeItem income) => Write(uid, data => data.Incomes.Add(income));

    public Task<bool> UpdateIncomeAsync(string uid, IncomeItem income) => Change(uid, data => Replace(data.Incomes, income, item => item.Id == income.Id));

    public Task<bool> DeleteIncomeAsync(string uid, string id) => Change(uid, data => data.Incomes.RemoveAll(item => item.Id == id) > 0);

    public Task AddBankAccountAsync(string uid, BankAccount account) => Write(uid, data => data.BankAccounts.Add(account));

    public Task<bool> UpdateBankAccountAsync(string uid, BankAccount account) =>
        Change(uid, data => Replace(data.BankAccounts, account, item => item.Id == account.Id));

    public Task<IReadOnlyList<IncomeSplit>> GetIncomeSplitsAsync(string uid) =>
        Read<IncomeSplit>(uid, data => data.IncomeSplits.Values);

    // Removes all splits from a month on, as a test setup can need.
    public Task ClearIncomeSplits(string uid) => Write(uid, data => data.IncomeSplits.Clear());

    public Task SetIncomeSplitAsync(string uid, IncomeSplit split, IReadOnlyList<string> replacedFromKeys) => Write(uid, data =>
    {
        foreach (var key in replacedFromKeys)
        {
            data.IncomeSplits.Remove(key);
        }
        data.IncomeSplits[split.FromPeriodKey] = split;
    });

    // Newest first; Transfers keeps the order they were added in, which stands in for createdAt.
    public Task<IReadOnlyList<Transfer>> GetTransfersAsync(string uid) =>
        Read<Transfer>(uid, data => data.Transfers
            .Select((transfer, added) => (transfer, added))
            .OrderByDescending(item => item.transfer.Date)
            .ThenByDescending(item => item.added)
            .Select(item => item.transfer));

    public async Task<Transfer?> GetTransferAsync(string uid, string id) =>
        (await Read<Transfer>(uid, data => data.Transfers.Where(transfer => transfer.Id == id))).SingleOrDefault();

    public Task AddTransferAsync(string uid, Transfer transfer) => Write(uid, data => data.Transfers.Add(transfer));

    public Task<bool> UpdateTransferAsync(string uid, Transfer transfer) =>
        Change(uid, data => Replace(data.Transfers, transfer, item => item.Id == transfer.Id));

    public Task<bool> DeleteTransferAsync(string uid, string id) =>
        Change(uid, data => data.Transfers.RemoveAll(transfer => transfer.Id == id) > 0);

    // Newest first, as transfers.
    public Task<IReadOnlyList<Loan>> GetLoansAsync(string uid) =>
        Read<Loan>(uid, data => data.Loans
            .Select((loan, added) => (loan, added))
            .OrderByDescending(item => item.loan.Date)
            .ThenByDescending(item => item.added)
            .Select(item => item.loan));

    public async Task<Loan?> GetLoanAsync(string uid, string id) =>
        (await Read<Loan>(uid, data => data.Loans.Where(loan => loan.Id == id))).SingleOrDefault();

    public Task AddLoanAsync(string uid, Loan loan) => Write(uid, data => data.Loans.Add(loan));

    public Task<bool> UpdateLoanAsync(string uid, Loan loan) =>
        Change(uid, data => Replace(data.Loans, loan, item => item.Id == loan.Id));

    public Task<bool> DeleteLoanAsync(string uid, string id) =>
        Change(uid, data => data.Loans.RemoveAll(loan => loan.Id == id) > 0);

    public Task<bool> UpdateCategoryAsync(string uid, Category category) =>
        Change(uid, data => Replace(data.Categories, category, item => item.Id == category.Id));

    public Task<bool> UpdateSubCategoryAsync(string uid, SubCategory subCategory) =>
        Change(uid, data => Replace(data.SubCategories, subCategory, item => item.Id == subCategory.Id));

    // Keeps the item's place in the list, as createdAt is kept in Firestore.
    private static bool Replace<T>(List<T> list, T item, Predicate<T> match)
    {
        var index = list.FindIndex(match);
        if (index < 0)
        {
            return false;
        }
        list[index] = item;
        return true;
    }

    private Task<IReadOnlyList<T>> Read<T>(string uid, Func<UserData, IEnumerable<T>> select)
    {
        if (!_users.TryGetValue(uid, out var data))
        {
            return Task.FromResult<IReadOnlyList<T>>([]);
        }
        lock (data)
        {
            return Task.FromResult<IReadOnlyList<T>>(select(data).ToList());
        }
    }

    private Task Write(string uid, Action<UserData> change)
    {
        var data = _users[uid];
        lock (data)
        {
            change(data);
        }
        return Task.CompletedTask;
    }

    // A write that may find nothing to change (an unknown user also finds nothing).
    private Task<bool> Change(string uid, Func<UserData, bool> change)
    {
        if (!_users.TryGetValue(uid, out var data))
        {
            return Task.FromResult(false);
        }
        lock (data)
        {
            return Task.FromResult(change(data));
        }
    }
}

// A clock the test can set (R-CAL-10).
public sealed class TestTime : TimeProvider
{
    // 17 Oct 2026, 12:00 in Riyadh (UTC+3): the PRD section 3 example.
    public static readonly DateTimeOffset Default = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; set; } = Default;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
