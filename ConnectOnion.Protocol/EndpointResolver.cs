using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConnectOnion.Protocol;

public sealed record ResolvedEndpoint(string HttpUrl, string WsUrl);

/// <summary>Relay record for an agent: online status and display name.</summary>
public sealed record AgentInfo(string Address, bool Online, string? Name);

/// <summary>
/// Relay lookup + direct-endpoint resolution. Port of <c>endpoint.ts</c>
/// (<c>resolveEndpoint</c>, <c>fetchAgentInfo</c>, <c>normalizeRelayUrl</c>,
/// <c>sortByProximity</c>). All fetch failures resolve to null/offline rather
/// than throwing — that is the contract, not swallowed bugs.
/// </summary>
public static class EndpointResolver
{
    public const string DefaultRelay = "wss://oo.openonion.ai";
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowDuplicateProperties = false,
    };

    public static string NormalizeRelayUrl(string relayUrl)
    {
        var normalized = relayUrl.TrimEnd('/');
        if (normalized.EndsWith("/ws/announce", StringComparison.Ordinal))
            normalized = normalized[..^"/ws/announce".Length];
        else if (normalized.EndsWith("/ws", StringComparison.Ordinal))
            normalized = normalized[..^"/ws".Length];
        return normalized;
    }

    private static string ToHttps(string relayUrl)
    {
        var normalized = NormalizeRelayUrl(relayUrl);
        if (normalized.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            return "https://" + normalized["wss://".Length..];
        if (normalized.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            return "http://" + normalized["ws://".Length..];
        return normalized;
    }

    // localhost first, then private ranges, then public — a caller is most
    // likely able to reach the closest endpoint.
    private static int Proximity(string url)
    {
        if (url.Contains("localhost") || url.Contains("127.0.0.1")) return 0;
        if (url.Contains("192.168.") || url.Contains("10.") || url.Contains("172.16.")) return 1;
        return 2;
    }

    /// <summary>
    /// Resolves an agent address to a directly reachable endpoint via the relay,
    /// or null if none is reachable (caller then uses the relay /ws/input path).
    /// </summary>
    public static async Task<ResolvedEndpoint?> ResolveEndpointAsync(
        HttpClient http, string agentAddress, string relayUrl, int timeoutMs = 3000)
    {
        var httpsRelay = ToHttps(relayUrl);

        var record = await GetJsonAsync(http, $"{httpsRelay}/api/relay/agents/{agentAddress}", timeoutMs)
            .ConfigureAwait(false);
        if (record is null || !record.Value.TryGetProperty("endpoints", out var endpointsEl) ||
            endpointsEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var httpEndpoints = endpointsEl.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => s is not null && s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Select(s => s!)
            .OrderBy(Proximity)
            .ToList();

        foreach (var httpUrl in httpEndpoints)
        {
            // Many advertised endpoints (localhost, docker IPs, NAT-bound public
            // IPs) fail from the caller's network; one failure must not abort.
            var info = await GetJsonAsync(http, $"{httpUrl}/info", timeoutMs).ConfigureAwait(false);
            if (info is not null &&
                info.Value.TryGetProperty("address", out var addr) &&
                addr.GetString() == agentAddress)
            {
                var isHttps = httpUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase);
                var baseUrl = httpUrl
                    .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("http://", "", StringComparison.OrdinalIgnoreCase);
                var scheme = isHttps ? "wss" : "ws";
                return new ResolvedEndpoint(httpUrl, $"{scheme}://{baseUrl}/ws");
            }
        }

        return null;
    }

    /// <summary>
    /// Fetches an agent's relay record for a connection test. Port of
    /// <c>fetchAgentInfo</c>: online iff the relay holds a live announce
    /// connection (the <c>relay</c> field), with the name from the published
    /// profile or a direct /info probe.
    /// </summary>
    public static async Task<AgentInfo> FetchAgentInfoAsync(
        HttpClient http, string agentAddress, string relayUrl, int timeoutMs = 5000)
    {
        var httpsRelay = ToHttps(relayUrl);

        var record = await GetJsonAsync(http, $"{httpsRelay}/api/relay/agents/{agentAddress}", timeoutMs)
            .ConfigureAwait(false);
        if (record is null) return new AgentInfo(agentAddress, false, null);

        // last_seen/endpoints persist forever in the DB, so only a non-null
        // `relay` (a live announce connection) means online.
        var online = record.Value.TryGetProperty("relay", out var relayEl) &&
                     relayEl.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

        string? name = null;
        if (record.Value.TryGetProperty("profile", out var profile) &&
            profile.ValueKind == JsonValueKind.Object)
        {
            name = GetName(profile);
        }

        if (record.Value.TryGetProperty("endpoints", out var endpointsEl) &&
            endpointsEl.ValueKind == JsonValueKind.Array)
        {
            var httpEndpoints = endpointsEl.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => s is not null && s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .Select(s => s!)
                .OrderBy(Proximity);

            foreach (var httpUrl in httpEndpoints)
            {
                var info = await GetJsonAsync(http, $"{httpUrl}/info", 3000).ConfigureAwait(false);
                if (info is not null &&
                    info.Value.TryGetProperty("address", out var addr) &&
                    addr.GetString() == agentAddress)
                {
                    return new AgentInfo(agentAddress, true, GetName(info.Value) ?? name);
                }
            }
        }

        return new AgentInfo(agentAddress, online, name);
    }

    private static string? GetName(JsonElement el)
    {
        foreach (var key in new[] { "name", "alias" })
        {
            if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }

    private static async Task<JsonElement?> GetJsonAsync(HttpClient http, string url, int timeoutMs)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var response = await http.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, JsonOptions, cts.Token).ConfigureAwait(false);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }
}
