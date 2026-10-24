using System.Security.Claims;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Features.Connect;

// MCP-05, STG-08: Settings → Connected apps. The list is part of GET /api/settings (NFR-01); Disconnect
// is the one call here. The id is a connection's, never a user id, and only your own can be removed.
public static class ConnectionsEndpoint
{
    // Dates in the profile time zone (YYYY-MM-DD). LastActiveOn is the last sign-in or token refresh.
    public record ConnectionDto(string Id, string ClientName, DateOnly ConnectedOn, DateOnly LastActiveOn);

    public record ConnectionsResponse(IReadOnlyList<ConnectionDto> Connections);

    public static IEndpointRouteBuilder MapConnections(this IEndpointRouteBuilder api)
    {
        // The app stops on its next call: its access token no longer finds a connection (McpTokenHandler).
        api.MapDelete("/connections/{id}", async (string id, ClaimsPrincipal user, IConnectionStore connections, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (!await connections.DeleteConnectionAsync(uid, id))
            {
                return Results.NotFound();
            }
            return Results.Ok(new ConnectionsResponse(await LoadAsync(uid, connections, time, await TimeZoneAsync(uid, store))));
        });
        return api;
    }

    // Newest first. A connection whose refresh token expired can never be used again, so it is removed
    // here (MCP-05) instead of being listed.
    public static async Task<IReadOnlyList<ConnectionDto>> LoadAsync(string uid, IConnectionStore connections, TimeProvider time, string timezone)
    {
        var now = time.GetUtcNow();
        var all = await connections.GetConnectionsAsync(uid);
        foreach (var expired in all.Where(connection => OAuth.IsExpired(connection.RefreshExpiresAt, now)))
        {
            await connections.DeleteConnectionAsync(uid, expired.Id);
        }
        return all
            .Where(connection => !OAuth.IsExpired(connection.RefreshExpiresAt, now))
            .OrderByDescending(connection => connection.CreatedAt)
            .Select(connection => new ConnectionDto(
                connection.Id,
                connection.ClientName,
                FiscalCalendar.DateIn(connection.CreatedAt, timezone),
                FiscalCalendar.DateIn(connection.LastUsedAt, timezone)))
            .ToList();
    }

    private static async Task<string> TimeZoneAsync(string uid, IUserStore store) =>
        (await store.GetProfileAsync(uid))?.Timezone ?? "UTC";
}
