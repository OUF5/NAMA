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

    // Archive comes with Settings (M8); tests add archived accounts here.
    public Task AddBankAccount(string uid, BankAccount account) => Write(uid, data => data.BankAccounts.Add(account));

    // Every entry of a user, as stored.
    public IReadOnlyList<Entry> Entries(string uid) => Read<Entry>(uid, data => data.Entries).Result;

    public Task<IReadOnlyList<FiscalYear>> GetFiscalYearsAsync(string uid) =>
        Task.FromResult<IReadOnlyList<FiscalYear>>(_users.TryGetValue(uid, out var data) ? [data.Setup.FiscalYear] : []);

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
        Read<IncomeItem>(uid, data => data.Setup.Incomes);

    public Task<IReadOnlyList<BankAccount>> GetBankAccountsAsync(string uid) =>
        Read<BankAccount>(uid, data => data.BankAccounts);

    public Task AddEntryAsync(string uid, Entry entry) => Write(uid, data =>
    {
        data.Entries.Add(entry);
        data.Profile = data.Profile with { LastBankAccountId = entry.BankAccountId };
    });

    public Task<IReadOnlyList<Entry>> GetEntriesAsync(string uid, string subCategoryId, string periodKey) =>
        Read<Entry>(uid, data => data.Entries.Where(entry => entry.SubCategoryId == subCategoryId && entry.PeriodKey == periodKey));

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
}

// A clock the test can set (R-CAL-10).
public sealed class TestTime : TimeProvider
{
    // 17 Oct 2026, 12:00 in Riyadh (UTC+3): the PRD section 3 example.
    public static readonly DateTimeOffset Default = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; set; } = Default;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
