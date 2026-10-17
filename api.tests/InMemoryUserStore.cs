using System.Collections.Concurrent;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Tests;

// Stands in for Firestore in endpoint tests (CI has no emulator). The Firestore store is
// checked by hand against the emulator.
public sealed class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<string, SetupData> _users = new();

    public SetupData? Saved(string uid) => _users.GetValueOrDefault(uid);

    public Task<UserProfile?> GetProfileAsync(string uid) =>
        Task.FromResult(_users.TryGetValue(uid, out var data) ? data.Profile : null);

    public Task<IReadOnlyList<FiscalYear>> GetFiscalYearsAsync(string uid) =>
        Task.FromResult<IReadOnlyList<FiscalYear>>(_users.TryGetValue(uid, out var data) ? [data.FiscalYear] : []);

    public Task<bool> CreateSetupAsync(string uid, SetupData setup) => Task.FromResult(_users.TryAdd(uid, setup));
}

// A clock the test can set (R-CAL-10).
public sealed class TestTime : TimeProvider
{
    // 17 Oct 2026, 12:00 in Riyadh (UTC+3): the PRD section 3 example.
    public static readonly DateTimeOffset Default = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; set; } = Default;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
