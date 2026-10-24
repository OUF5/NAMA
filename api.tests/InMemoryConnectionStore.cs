using Api.Infrastructure;

namespace Api.Tests;

// Stands in for Firestore's oauthCodes and connections (MCP-02, MCP-03). CreatedAt is the time given
// (TestTime), as the server timestamp would be.
public sealed class InMemoryConnectionStore(TimeProvider time) : IConnectionStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AuthorizationCode> _codes = [];
    private readonly Dictionary<string, Connection> _connections = [];

    public IReadOnlyList<AuthorizationCode> Codes
    {
        get { lock (_lock) return [.. _codes.Values]; }
    }

    public IReadOnlyList<Connection> Connections
    {
        get { lock (_lock) return [.. _connections.Values]; }
    }

    public Task CreateCodeAsync(AuthorizationCode code)
    {
        lock (_lock) _codes.Add(code.Id, code);
        return Task.CompletedTask;
    }

    // As the Firestore transaction: read and delete in one step.
    public Task<AuthorizationCode?> TakeCodeAsync(string id)
    {
        lock (_lock) return Task.FromResult(_codes.Remove(id, out var code) ? code : null);
    }

    public Task CreateConnectionAsync(Connection connection)
    {
        lock (_lock) _connections.Add(connection.Id, connection with { CreatedAt = time.GetUtcNow() });
        return Task.CompletedTask;
    }

    public Task<Connection?> FindByAccessTokenAsync(string accessTokenHash)
    {
        lock (_lock) return Task.FromResult(_connections.Values.FirstOrDefault(connection => connection.AccessTokenHash == accessTokenHash));
    }

    public Task<Connection?> RefreshAsync(string refreshTokenHash, Func<Connection, Connection?> change)
    {
        lock (_lock)
        {
            if (_connections.Values.FirstOrDefault(connection => connection.RefreshTokenHash == refreshTokenHash) is not { } found
                || change(found) is not { } changed)
            {
                return Task.FromResult<Connection?>(null);
            }
            _connections[found.Id] = changed with { CreatedAt = found.CreatedAt };
            return Task.FromResult<Connection?>(changed);
        }
    }

    public Task<IReadOnlyList<Connection>> GetConnectionsAsync(string uid)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<Connection>>(_connections.Values.Where(connection => connection.Uid == uid).ToList());
    }

    public Task<bool> DeleteConnectionAsync(string uid, string id)
    {
        lock (_lock)
        {
            return Task.FromResult(_connections.TryGetValue(id, out var connection) && connection.Uid == uid && _connections.Remove(id));
        }
    }
}
