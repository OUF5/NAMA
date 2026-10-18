using Api.Domain;
using Api.Infrastructure;

namespace Api.Tests;

// Stands in for Firestore's invites and members (AUTH-06, AUTH-07). It counts how often the members
// are read, so tests can prove that the access check reads no Firestore on a normal request.
public sealed class InMemoryAccessStore : IAccessStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Invite> _invites = [];
    private readonly List<Member> _members = [];
    private int _memberLoads;

    public int MemberLoads => Volatile.Read(ref _memberLoads);

    // Makes GetMembersAsync throw, as when Firestore cannot be reached.
    public bool FailMemberLoads { get; set; }

    public IReadOnlyList<Member> Members
    {
        get { lock (_lock) return [.. _members]; }
    }

    public IReadOnlyList<Invite> Invites
    {
        get { lock (_lock) return [.. _invites.Values]; }
    }

    public Task<IReadOnlyList<Member>> GetMembersAsync()
    {
        Interlocked.Increment(ref _memberLoads);
        if (FailMemberLoads)
        {
            throw new InvalidOperationException("Firestore cannot be reached.");
        }
        return Task.FromResult(Members);
    }

    public Task<IReadOnlyList<Invite>> GetInvitesAsync() => Task.FromResult(Invites);

    public Task CreateInviteAsync(Invite invite)
    {
        lock (_lock) _invites.Add(invite.Id, invite);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteInviteAsync(string id)
    {
        lock (_lock) return Task.FromResult(_invites.Remove(id));
    }

    // As the Firestore transaction: the invite goes and the member comes in one step.
    public Task<(AcceptResult Result, Member? Member)> AcceptInviteAsync(string inviteId, string uid, string email, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_invites.TryGetValue(inviteId, out var invite))
            {
                return Task.FromResult<(AcceptResult, Member?)>((AcceptResult.NotFound, null));
            }
            if (Api.Domain.Invites.IsExpired(invite.ExpiresAt, now))
            {
                return Task.FromResult<(AcceptResult, Member?)>((AcceptResult.Expired, null));
            }
            _invites.Remove(inviteId);
            var member = new Member(DocumentIds.New(), uid, email, now, invite.CreatedByEmail);
            _members.Add(member);
            return Task.FromResult<(AcceptResult, Member?)>((AcceptResult.Joined, member));
        }
    }

    public Task<Member?> DeleteMemberAsync(string id)
    {
        lock (_lock)
        {
            var member = _members.SingleOrDefault(stored => stored.Id == id);
            if (member is not null)
            {
                _members.Remove(member);
            }
            return Task.FromResult(member);
        }
    }
}
