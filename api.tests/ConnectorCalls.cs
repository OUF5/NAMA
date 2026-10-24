using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// The Claude connector as Claude Code uses it (MCP-01 to MCP-03): register, Connect (as the signed-in
// person), the token exchange, and MCP calls with the access token.
public static class ConnectorCalls
{
    public const string Callback = "http://localhost:33418/callback";
    public const string Resource = ApiFactory.Origin + "/api/mcp";

    public sealed record Pkce(string Verifier, string Challenge)
    {
        public static Pkce New()
        {
            var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            return new Pkce(verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        }
    }

    public sealed record Tokens(string ClientId, string AccessToken, string RefreshToken);

    public static async Task<HttpResponseMessage> Register(ApiFactory factory, string callback = Callback, string? name = "Claude Code (nama)") =>
        await factory.CreateClient().PostAsJsonAsync("/api/oauth/register",
            new JsonObject { ["redirect_uris"] = new JsonArray(callback), ["client_name"] = name, ["token_endpoint_auth_method"] = "none" });

    public static async Task<string> ClientId(ApiFactory factory, string callback = Callback)
    {
        var response = await Register(factory, callback);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response))["client_id"]!.GetValue<string>();
    }

    // Connect's request, as it reads it from its address.
    public static JsonObject Request(string clientId, Pkce pkce, string callback = Callback, bool allow = true) => new()
    {
        ["responseType"] = "code",
        ["clientId"] = clientId,
        ["redirectUri"] = callback,
        ["codeChallenge"] = pkce.Challenge,
        ["codeChallengeMethod"] = "S256",
        ["state"] = "state-123",
        ["scope"] = "nama.read",
        ["resource"] = Resource,
        ["allow"] = allow,
    };

    // Allow as the signed-in person; returns the code from the redirect.
    public static async Task<string> Allow(ApiFactory factory, string sub, string clientId, Pkce pkce, string email = ApiFactory.AllowedEmail)
    {
        var response = await Send(factory, HttpMethod.Post, "/api/oauth/authorize", sub, Request(clientId, pkce), email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = QueryHelpers.ParseQuery(new Uri((await Json(response))["redirectTo"]!.GetValue<string>()).Query);
        Assert.Equal("state-123", query["state"].ToString());
        return query["code"].ToString();
    }

    public static async Task<HttpResponseMessage> Token(ApiFactory factory, Dictionary<string, string> form) =>
        await factory.CreateClient().PostAsync("/api/oauth/token", new FormUrlEncodedContent(form));

    public static Dictionary<string, string> CodeForm(string code, string clientId, Pkce pkce, string callback = Callback) => new()
    {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["client_id"] = clientId,
        ["redirect_uri"] = callback,
        ["code_verifier"] = pkce.Verifier,
        ["resource"] = Resource,
    };

    public static Dictionary<string, string> RefreshForm(Tokens tokens) => new()
    {
        ["grant_type"] = "refresh_token",
        ["refresh_token"] = tokens.RefreshToken,
        ["client_id"] = tokens.ClientId,
        ["resource"] = Resource,
    };

    public static async Task<Tokens> ReadTokens(HttpResponseMessage response, string clientId)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(("Bearer", 3600, "nama.read"),
            (body["token_type"]!.GetValue<string>(), body["expires_in"]!.GetValue<long>(), body["scope"]!.GetValue<string>()));
        return new Tokens(clientId, body["access_token"]!.GetValue<string>(), body["refresh_token"]!.GetValue<string>());
    }

    // The whole sign-in, as Claude Code does it.
    public static async Task<Tokens> Connect(ApiFactory factory, string sub, string email = ApiFactory.AllowedEmail)
    {
        var clientId = await ClientId(factory);
        var pkce = Pkce.New();
        var code = await Allow(factory, sub, clientId, pkce, email);
        return await ReadTokens(await Token(factory, CodeForm(code, clientId, pkce)), clientId);
    }

    public static async Task<string?> OAuthError(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await Json(response))["error"]!.GetValue<string>();
    }

    // One JSON-RPC call to /api/mcp (stateless Streamable HTTP). The answer may come as JSON or as one
    // server-sent event.
    public static async Task<HttpResponseMessage> McpSend(ApiFactory factory, string? token, string method, JsonObject? parameters = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = method,
                ["params"] = parameters ?? new JsonObject(),
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await factory.CreateClient().SendAsync(request);
    }

    public static async Task<JsonNode> McpResult(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').Select(line => line.TrimEnd('\r')).Last(line => line.StartsWith("data:"))["data:".Length..]
            : text;
        var message = JsonNode.Parse(json)!;
        Assert.Null(message["error"]);
        return message["result"]!;
    }

    public static async Task<JsonNode> CallTool(ApiFactory factory, string token, string name, JsonObject? arguments = null) =>
        await McpResult(await McpSend(factory, token, "tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments ?? new JsonObject() }));

    // A tool's answer as JSON: the structured content when there is one, else the first text block.
    public static JsonNode ToolJson(JsonNode result) =>
        result["structuredContent"] ?? JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!;
}
