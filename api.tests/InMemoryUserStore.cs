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

    public Task<IReadOnlyList<FiscalYear>> GetFiscalYearsAsync(string uid) => Read<FiscalYear>(uid, data => data.FiscalYears);

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

    public Task<IReadOnlyList<Entry>> GetEntriesAsync(string uid, string subCategoryId, string periodKey) =>
        Read<Entry>(uid, data => data.Entries.Where(entry => entry.SubCategoryId == subCategoryId && entry.PeriodKey == periodKey));

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

    public Task UpdateCalendarAsync(string uid, int fiscalStartMonth, int paydayDay) =>
        Write(uid, data => data.Profile = data.Profile with { FiscalStartMonth = fiscalStartMonth, PaydayDay = paydayDay });

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
