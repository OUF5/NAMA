using Api.Domain;
using Google.Cloud.Firestore;

namespace Api.Infrastructure;

// AUTH-06: an unused invite link. Id is the SHA-256 of the link's token (Invites.Hash).
public sealed record Invite(string Id, string CreatedByUid, string CreatedByEmail, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

// AUTH-02: a person who joined with an invite link. Id is its own random id, so the API never takes
// a user id from a URL; Uid is the token "sub" of the Google account that used the link.
public sealed record Member(string Id, string Uid, string Email, DateTimeOffset JoinedAt, string InvitedByEmail);

public enum AcceptResult
{
    Joined,
    // Unknown, already used or revoked.
    NotFound,
    Expired,
}

// The app-level access data (PRD 7.4): top-level invites/{id} and members/{id}, not under users/{uid}.
public interface IAccessStore
{
    Task<IReadOnlyList<Member>> GetMembersAsync();

    Task<IReadOnlyList<Invite>> GetInvitesAsync();

    Task CreateInviteAsync(Invite invite);

    // Revoke. False when the link is no longer there.
    Task<bool> DeleteInviteAsync(string id);

    // In one transaction the invite is deleted and the member created, so a link works once even
    // when two people accept at the same moment.
    Task<(AcceptResult Result, Member? Member)> AcceptInviteAsync(string inviteId, string uid, string email, DateTimeOffset now);

    // The removed member, or null when there is none. Their users/{uid} data is not touched.
    Task<Member?> DeleteMemberAsync(string id);
}

public sealed class FirestoreAccessStore(FirestoreDb db) : IAccessStore
{
    private CollectionReference Invites => db.Collection("invites");
    private CollectionReference Members => db.Collection("members");

    public async Task<IReadOnlyList<Member>> GetMembersAsync()
    {
        var snapshot = await Members.GetSnapshotAsync();
        return snapshot.Documents.Select(document => AccessDocuments.ToMember(document.Id, document.ConvertTo<MemberDocument>())).ToList();
    }

    public async Task<IReadOnlyList<Invite>> GetInvitesAsync()
    {
        var snapshot = await Invites.GetSnapshotAsync();
        return snapshot.Documents.Select(document => AccessDocuments.ToInvite(document.Id, document.ConvertTo<InviteDocument>())).ToList();
    }

    public Task CreateInviteAsync(Invite invite) => Invites.Document(invite.Id).CreateAsync(AccessDocuments.ToDocument(invite));

    public Task<bool> DeleteInviteAsync(string id) => FirestoreUserStore.DeleteAsync(Invites.Document(id));

    // The transaction may run again after a conflict, so the member id is made inside it.
    public Task<(AcceptResult Result, Member? Member)> AcceptInviteAsync(string inviteId, string uid, string email, DateTimeOffset now) =>
        db.RunTransactionAsync<(AcceptResult, Member?)>(async transaction =>
        {
            var reference = Invites.Document(inviteId);
            var snapshot = await transaction.GetSnapshotAsync(reference);
            if (!snapshot.Exists)
            {
                return (AcceptResult.NotFound, null);
            }
            var invite = AccessDocuments.ToInvite(snapshot.Id, snapshot.ConvertTo<InviteDocument>());
            if (Api.Domain.Invites.IsExpired(invite.ExpiresAt, now))
            {
                return (AcceptResult.Expired, null);
            }
            var member = new Member(DocumentIds.New(), uid, email, now, invite.CreatedByEmail);
            transaction.Delete(reference);
            transaction.Create(Members.Document(member.Id), AccessDocuments.ToDocument(member));
            return (AcceptResult.Joined, member);
        });

    public Task<Member?> DeleteMemberAsync(string id) =>
        db.RunTransactionAsync(async transaction =>
        {
            var reference = Members.Document(id);
            var snapshot = await transaction.GetSnapshotAsync(reference);
            if (!snapshot.Exists)
            {
                return null;
            }
            transaction.Delete(reference);
            return AccessDocuments.ToMember(snapshot.Id, snapshot.ConvertTo<MemberDocument>());
        });
}

[FirestoreData]
public sealed class InviteDocument
{
    [FirestoreProperty("createdByUid")] public string CreatedByUid { get; set; } = "";
    [FirestoreProperty("createdByEmail")] public string CreatedByEmail { get; set; } = "";
    [FirestoreProperty("expiresAt")] public Timestamp ExpiresAt { get; set; }
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

[FirestoreData]
public sealed class MemberDocument
{
    [FirestoreProperty("uid")] public string Uid { get; set; } = "";
    [FirestoreProperty("email")] public string Email { get; set; } = "";
    [FirestoreProperty("joinedAt")] public Timestamp JoinedAt { get; set; }
    [FirestoreProperty("invitedByEmail")] public string InvitedByEmail { get; set; } = "";
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

// Moments (expiresAt, joinedAt) are Timestamps like createdAt; they become dates only for display.
public static class AccessDocuments
{
    public static InviteDocument ToDocument(Invite invite) => new()
    {
        CreatedByUid = invite.CreatedByUid,
        CreatedByEmail = invite.CreatedByEmail,
        ExpiresAt = Timestamp.FromDateTimeOffset(invite.ExpiresAt),
    };

    public static Invite ToInvite(string id, InviteDocument document) => new(
        id, document.CreatedByUid, document.CreatedByEmail, document.CreatedAt.ToDateTimeOffset(), document.ExpiresAt.ToDateTimeOffset());

    public static MemberDocument ToDocument(Member member) => new()
    {
        Uid = member.Uid,
        Email = member.Email,
        JoinedAt = Timestamp.FromDateTimeOffset(member.JoinedAt),
        InvitedByEmail = member.InvitedByEmail,
    };

    public static Member ToMember(string id, MemberDocument document) => new(
        id, document.Uid, document.Email, document.JoinedAt.ToDateTimeOffset(), document.InvitedByEmail);
}
