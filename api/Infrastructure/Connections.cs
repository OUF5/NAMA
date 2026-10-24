using Google.Cloud.Firestore;

namespace Api.Infrastructure;

// MCP-02: an authorization code from Connect. Id is the SHA-256 of the code (OAuth.Hash); the code
// itself is only in the redirect to the app.
public sealed record AuthorizationCode(
    string Id,
    string Uid,
    string Email,
    string ClientId,
    string ClientName,
    string RedirectUri,
    string CodeChallenge,
    string Scope,
    DateTimeOffset ExpiresAt);

// MCP-03, MCP-05: one app's access, from Allow until Disconnect. Only the hashes of its current access
// and refresh tokens are stored. Id is random, so no URL carries a user id. LastUsedAt is the last
// sign-in or refresh; CreatedAt is set by the store.
public sealed record Connection(
    string Id,
    string Uid,
    string Email,
    string ClientId,
    string ClientName,
    string Scope,
    string AccessTokenHash,
    DateTimeOffset AccessExpiresAt,
    string RefreshTokenHash,
    DateTimeOffset RefreshExpiresAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset CreatedAt = default);

// App-level data (PRD 7.4): top-level oauthCodes/{id} and connections/{id}, found by token hash.
public interface IConnectionStore
{
    Task CreateCodeAsync(AuthorizationCode code);

    // Reads and deletes it in one transaction, so a code works once even when it is sent twice at the
    // same moment. Null when unknown or already used; an expired one is returned (and gone).
    Task<AuthorizationCode?> TakeCodeAsync(string id);

    Task CreateConnectionAsync(Connection connection);

    Task<Connection?> FindByAccessTokenAsync(string accessTokenHash);

    // In one transaction: finds the connection of this refresh token, and stores what change makes of it
    // (the new token hashes). Null when unknown, or when change returns null (nothing is stored then).
    Task<Connection?> RefreshAsync(string refreshTokenHash, Func<Connection, Connection?> change);

    Task<IReadOnlyList<Connection>> GetConnectionsAsync(string uid);

    // Only the user's own connection; false when there is none.
    Task<bool> DeleteConnectionAsync(string uid, string id);
}

public sealed class FirestoreConnectionStore(FirestoreDb db) : IConnectionStore
{
    private CollectionReference Codes => db.Collection("oauthCodes");
    private CollectionReference Connections => db.Collection("connections");

    public Task CreateCodeAsync(AuthorizationCode code) => Codes.Document(code.Id).CreateAsync(ConnectionDocuments.ToDocument(code));

    public Task<AuthorizationCode?> TakeCodeAsync(string id) =>
        db.RunTransactionAsync(async transaction =>
        {
            var reference = Codes.Document(id);
            var snapshot = await transaction.GetSnapshotAsync(reference);
            if (!snapshot.Exists)
            {
                return null;
            }
            transaction.Delete(reference);
            return ConnectionDocuments.ToCode(snapshot.Id, snapshot.ConvertTo<AuthorizationCodeDocument>());
        });

    public Task CreateConnectionAsync(Connection connection) =>
        Connections.Document(connection.Id).CreateAsync(ConnectionDocuments.ToDocument(connection));

    public async Task<Connection?> FindByAccessTokenAsync(string accessTokenHash)
    {
        var snapshot = await Connections.WhereEqualTo("accessTokenHash", accessTokenHash).Limit(1).GetSnapshotAsync();
        return snapshot.Documents.Select(ToConnection).FirstOrDefault();
    }

    // Only the token fields change, so createdAt is kept.
    public Task<Connection?> RefreshAsync(string refreshTokenHash, Func<Connection, Connection?> change) =>
        db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(Connections.WhereEqualTo("refreshTokenHash", refreshTokenHash).Limit(1));
            if (snapshot.Documents.FirstOrDefault() is not { } document || change(ToConnection(document)) is not { } changed)
            {
                return null;
            }
            transaction.Update(document.Reference, new Dictionary<string, object>
            {
                ["accessTokenHash"] = changed.AccessTokenHash,
                ["accessExpiresAt"] = Timestamp.FromDateTimeOffset(changed.AccessExpiresAt),
                ["refreshTokenHash"] = changed.RefreshTokenHash,
                ["refreshExpiresAt"] = Timestamp.FromDateTimeOffset(changed.RefreshExpiresAt),
                ["lastUsedAt"] = Timestamp.FromDateTimeOffset(changed.LastUsedAt),
                ["updatedAt"] = FieldValue.ServerTimestamp,
            });
            return changed;
        });

    public async Task<IReadOnlyList<Connection>> GetConnectionsAsync(string uid)
    {
        var snapshot = await Connections.WhereEqualTo("uid", uid).GetSnapshotAsync();
        return snapshot.Documents.Select(ToConnection).ToList();
    }

    public Task<bool> DeleteConnectionAsync(string uid, string id) =>
        db.RunTransactionAsync(async transaction =>
        {
            var reference = Connections.Document(id);
            var snapshot = await transaction.GetSnapshotAsync(reference);
            if (!snapshot.Exists || snapshot.GetValue<string>("uid") != uid)
            {
                return false;
            }
            transaction.Delete(reference);
            return true;
        });

    private static Connection ToConnection(DocumentSnapshot snapshot) =>
        ConnectionDocuments.ToConnection(snapshot.Id, snapshot.ConvertTo<ConnectionDocument>());
}

[FirestoreData]
public sealed class AuthorizationCodeDocument
{
    [FirestoreProperty("uid")] public string Uid { get; set; } = "";
    [FirestoreProperty("email")] public string Email { get; set; } = "";
    [FirestoreProperty("clientId")] public string ClientId { get; set; } = "";
    [FirestoreProperty("clientName")] public string ClientName { get; set; } = "";
    [FirestoreProperty("redirectUri")] public string RedirectUri { get; set; } = "";
    [FirestoreProperty("codeChallenge")] public string CodeChallenge { get; set; } = "";
    [FirestoreProperty("scope")] public string Scope { get; set; } = "";
    [FirestoreProperty("expiresAt")] public Timestamp ExpiresAt { get; set; }
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

[FirestoreData]
public sealed class ConnectionDocument
{
    [FirestoreProperty("uid")] public string Uid { get; set; } = "";
    [FirestoreProperty("email")] public string Email { get; set; } = "";
    [FirestoreProperty("clientId")] public string ClientId { get; set; } = "";
    [FirestoreProperty("clientName")] public string ClientName { get; set; } = "";
    [FirestoreProperty("scope")] public string Scope { get; set; } = "";
    [FirestoreProperty("accessTokenHash")] public string AccessTokenHash { get; set; } = "";
    [FirestoreProperty("accessExpiresAt")] public Timestamp AccessExpiresAt { get; set; }
    [FirestoreProperty("refreshTokenHash")] public string RefreshTokenHash { get; set; } = "";
    [FirestoreProperty("refreshExpiresAt")] public Timestamp RefreshExpiresAt { get; set; }
    [FirestoreProperty("lastUsedAt")] public Timestamp LastUsedAt { get; set; }
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

public static class ConnectionDocuments
{
    public static AuthorizationCodeDocument ToDocument(AuthorizationCode code) => new()
    {
        Uid = code.Uid,
        Email = code.Email,
        ClientId = code.ClientId,
        ClientName = code.ClientName,
        RedirectUri = code.RedirectUri,
        CodeChallenge = code.CodeChallenge,
        Scope = code.Scope,
        ExpiresAt = Timestamp.FromDateTimeOffset(code.ExpiresAt),
    };

    public static AuthorizationCode ToCode(string id, AuthorizationCodeDocument document) => new(
        id, document.Uid, document.Email, document.ClientId, document.ClientName, document.RedirectUri, document.CodeChallenge,
        document.Scope, document.ExpiresAt.ToDateTimeOffset());

    public static ConnectionDocument ToDocument(Connection connection) => new()
    {
        Uid = connection.Uid,
        Email = connection.Email,
        ClientId = connection.ClientId,
        ClientName = connection.ClientName,
        Scope = connection.Scope,
        AccessTokenHash = connection.AccessTokenHash,
        AccessExpiresAt = Timestamp.FromDateTimeOffset(connection.AccessExpiresAt),
        RefreshTokenHash = connection.RefreshTokenHash,
        RefreshExpiresAt = Timestamp.FromDateTimeOffset(connection.RefreshExpiresAt),
        LastUsedAt = Timestamp.FromDateTimeOffset(connection.LastUsedAt),
    };

    public static Connection ToConnection(string id, ConnectionDocument document) => new(
        id, document.Uid, document.Email, document.ClientId, document.ClientName, document.Scope,
        document.AccessTokenHash, document.AccessExpiresAt.ToDateTimeOffset(),
        document.RefreshTokenHash, document.RefreshExpiresAt.ToDateTimeOffset(),
        document.LastUsedAt.ToDateTimeOffset(), document.CreatedAt.ToDateTimeOffset());
}
