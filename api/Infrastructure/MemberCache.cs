namespace Api.Infrastructure;

// AUTH-02 without a Firestore read on every request: the uids of the people who joined with an
// invite link, kept in memory. Every join and removal goes through this one instance (Cloud Run max
// instances = 1, NFR-04), so it changes the set right after its write. The set is also read again at
// most every 5 minutes, and only when a request needs it, which covers a second instance running
// for a moment (for example the old revision during a deploy).
public sealed class MemberCache(IAccessStore store, TimeProvider time)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private sealed record Snapshot(IReadOnlySet<string> Uids, DateTimeOffset LoadedAt);

    // Loads and changes take turns, so a load that started before a join commits cannot drop the
    // new member: the change is applied after the load. Reads use the current snapshot without it.
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile Snapshot? _snapshot;

    public async Task<bool> ContainsAsync(string uid)
    {
        var snapshot = _snapshot;
        if (snapshot is null || IsStale(snapshot))
        {
            snapshot = await LoadAsync();
        }
        return snapshot.Uids.Contains(uid);
    }

    public Task AddAsync(string uid) => ChangeAsync(uids => uids.Add(uid));

    public Task RemoveAsync(string uid) => ChangeAsync(uids => uids.Remove(uid));

    private bool IsStale(Snapshot snapshot) => time.GetUtcNow() - snapshot.LoadedAt >= MaxAge;

    private async Task<Snapshot> LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            // Another request may have loaded it while this one waited.
            if (_snapshot is { } current && !IsStale(current))
            {
                return current;
            }
            var members = await store.GetMembersAsync();
            var loaded = new Snapshot(members.Select(member => member.Uid).ToHashSet(StringComparer.Ordinal), time.GetUtcNow());
            _snapshot = loaded;
            return loaded;
        }
        finally
        {
            _lock.Release();
        }
    }

    // Before the first load there is nothing to change: that load reads the committed write.
    private async Task ChangeAsync(Action<HashSet<string>> change)
    {
        await _lock.WaitAsync();
        try
        {
            if (_snapshot is { } current)
            {
                var uids = new HashSet<string>(current.Uids, StringComparer.Ordinal);
                change(uids);
                _snapshot = current with { Uids = uids };
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
