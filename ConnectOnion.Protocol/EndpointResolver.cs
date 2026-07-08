using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConnectOnion.Protocol;

public sealed record ResolvedEndpoint(string HttpUrl, string WsUrl);

/// <summary>A single skill entry from the agent's published profile or /info.</summary>
public sealed record SkillInfo(string Name, string Description, string? Location = null);

/// <summary>Accepted file input constraints from the agent's /info.</summary>
public sealed record AgentFileInputs(int MaxFileSizeMb, int MaxFilesPerRequest);

/// <summary>Accepted input types the agent advertises.</summary>
public sealed record AgentAcceptedInputs(
    bool? Text = null,
    bool? Images = null,
    AgentFileInputs? Files = null);

/// <summary>
/// Full agent metadata returned by <c>fetchAgentInfo</c>. Port of the TypeScript
/// <c>AgentInfo</c> interface in <c>types.ts</c>.
/// </summary>
public sealed record AgentInfo(
    string Address,
    bool Online,
    string? Name = null,
    IReadOnlyList<string>? Tools = null,
    IReadOnlyList<SkillInfo>? Skills = null,
    string? Trust = null,
    string? Version = null,
    string? Model = null,
    AgentAcceptedInputs? AcceptedInputs = null);

/// <summary>
/// Relay lookup + direct-endpoint resolution. Port of <c>endpoint.ts</c>
/// (<c>resolveEndpoint</c>, <c>fetchAgentInfo</c>, <c>normalizeRelayUrl</c>,
/// <c>sortByProximity</c>). All fetch failures resolve to null/offline rather
/// than throwing — that is the contract, not swallowed bugs.
/// </summary>
public static class EndpointResolver
{
    public const string DefaultRelay = "wss://oo.openonion.ai";
    private static readonly TimeSpan EndpointCacheTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan AgentInfoOnlineCacheTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan AgentInfoOfflineCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DirectInfoCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, EndpointCacheEntry> EndpointCache = new();
    private static readonly Dictionary<string, AgentInfoCacheEntry> AgentInfoCache = new();
    private static readonly Dictionary<string, DirectInfoCacheEntry> DirectInfoCache = new();
    private static readonly Dictionary<string, Task<ResolvedEndpoint?>> EndpointInflight = new();
    private static readonly Dictionary<string, Task<AgentInfo>> AgentInfoInflight = new();
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowDuplicateProperties = false,
    };

    private sealed record EndpointCacheEntry(ResolvedEndpoint? Endpoint, DateTimeOffset CheckedAt);
    private sealed record AgentInfoCacheEntry(AgentInfo Info, DateTimeOffset CheckedAt);
    private sealed record DirectInfoCacheEntry(string EndpointUrl, DateTimeOffset CheckedAt);

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
        HttpClient http, string agentAddress, string relayUrl, int timeoutMs = 3000,
        bool forceRefresh = false)
    {
        var key = $"{NormalizeRelayUrl(relayUrl)}|{agentAddress}";
        Task<ResolvedEndpoint?> task;
        var ownsTask = false;

        lock (CacheGate)
        {
            if (!forceRefresh &&
                EndpointCache.TryGetValue(key, out var cached) &&
                DateTimeOffset.UtcNow - cached.CheckedAt < EndpointCacheTtl)
            {
                return cached.Endpoint;
            }

            if (EndpointInflight.TryGetValue(key, out var running))
            {
                task = running;
            }
            else
            {
                task = ResolveEndpointUncachedAsync(http, agentAddress, relayUrl, timeoutMs);
                EndpointInflight[key] = task;
                ownsTask = true;
            }
        }

        if (!ownsTask) return await task.ConfigureAwait(false);
        return await CompleteEndpointAsync(key, task).ConfigureAwait(false);
    }

    private static async Task<ResolvedEndpoint?> CompleteEndpointAsync(string key, Task<ResolvedEndpoint?> task)
    {
        try
        {
            var endpoint = await task.ConfigureAwait(false);
            lock (CacheGate) EndpointCache[key] = new EndpointCacheEntry(endpoint, DateTimeOffset.UtcNow);
            return endpoint;
        }
        finally
        {
            lock (CacheGate) EndpointInflight.Remove(key);
        }
    }

    private static async Task<ResolvedEndpoint?> ResolveEndpointUncachedAsync(
        HttpClient http, string agentAddress, string relayUrl, int timeoutMs)
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
    /// Fetches full agent metadata through the relay and optional /info probes.
    /// Port of <c>fetchAgentInfo</c> in <c>endpoint.ts</c>: relay profile provides
    /// the base, /info probes on direct endpoints merge richer data on top.
    /// </summary>
    public static async Task<AgentInfo> FetchAgentInfoAsync(
        HttpClient http, string agentAddress, string relayUrl, int timeoutMs = 5000,
        bool forceRefresh = false)
    {
        var key = $"{NormalizeRelayUrl(relayUrl)}|{agentAddress}";
        Task<AgentInfo> task;
        var ownsTask = false;

        lock (CacheGate)
        {
            if (!forceRefresh &&
                AgentInfoCache.TryGetValue(key, out var cached) &&
                DateTimeOffset.UtcNow - cached.CheckedAt < (cached.Info.Online ? AgentInfoOnlineCacheTtl : AgentInfoOfflineCacheTtl))
            {
                return cached.Info;
            }

            if (AgentInfoInflight.TryGetValue(key, out var running))
            {
                task = running;
            }
            else
            {
                task = FetchAgentInfoUncachedAsync(http, agentAddress, relayUrl, timeoutMs);
                AgentInfoInflight[key] = task;
                ownsTask = true;
            }
        }

        if (!ownsTask) return await task.ConfigureAwait(false);
        return await CompleteAgentInfoAsync(key, task).ConfigureAwait(false);
    }

    private static async Task<AgentInfo> CompleteAgentInfoAsync(string key, Task<AgentInfo> task)
    {
        try
        {
            var info = await task.ConfigureAwait(false);
            lock (CacheGate) AgentInfoCache[key] = new AgentInfoCacheEntry(info, DateTimeOffset.UtcNow);
            return info;
        }
        finally
        {
            lock (CacheGate) AgentInfoInflight.Remove(key);
        }
    }

    private static async Task<AgentInfo> FetchAgentInfoUncachedAsync(
        HttpClient http, string agentAddress, string relayUrl, int timeoutMs)
    {
        var httpsRelay = ToHttps(relayUrl);

        var record = await GetJsonAsync(http, $"{httpsRelay}/api/relay/agents/{agentAddress}", timeoutMs)
            .ConfigureAwait(false);
        if (record is null) return new AgentInfo(agentAddress, false);

        var online = record.Value.TryGetProperty("relay", out var relayEl) &&
                     relayEl.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

        var fallback = BuildAgentInfo(agentAddress, online, record.Value);

        AgentInfo? profileInfo = null;
        if (record.Value.TryGetProperty("profile", out var profileEl) &&
            profileEl.ValueKind == JsonValueKind.Object)
        {
            profileInfo = ParseInfoSource(profileEl);
        }

        if (!record.Value.TryGetProperty("endpoints", out var endpointsEl) ||
            endpointsEl.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var httpEndpoints = endpointsEl.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => s is not null && s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Select(s => s!)
            .OrderBy(Proximity);

        // Try the last-known-good endpoint first (cached, 5 min TTL) so a
        // repeat info fetch against the same agent skips the parallel probe
        // entirely when the cached endpoint is still reachable.
        string? cachedEndpoint = null;
        lock (CacheGate)
        {
            if (DirectInfoCache.TryGetValue(agentAddress, out var cached) &&
                DateTimeOffset.UtcNow - cached.CheckedAt < DirectInfoCacheTtl)
            {
                cachedEndpoint = cached.EndpointUrl;
            }
        }

        if (cachedEndpoint is not null)
        {
            var cachedInfo = await GetJsonAsync(http, $"{cachedEndpoint}/info", 3000).ConfigureAwait(false);
            if (cachedInfo is not null &&
                cachedInfo.Value.TryGetProperty("address", out var cachedAddr) &&
                cachedAddr.GetString() == agentAddress)
            {
                var directInfo = ParseInfoSource(cachedInfo.Value);
                return MergeAgentInfo(fallback, profileInfo, directInfo, online: true);
            }
        }

        // Probe all endpoints in parallel; return on the first match so the
        // worst case is ~3 s instead of N × 3 s when most endpoints are
        // unreachable from the caller's network.
        var pending = httpEndpoints
            .Select(async httpUrl =>
            {
                var info = await GetJsonAsync(http, $"{httpUrl}/info", 3000).ConfigureAwait(false);
                return (httpUrl, info);
            })
            .ToList();

        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(done);
            var (httpUrl, info) = await done.ConfigureAwait(false);
            if (info is not null &&
                info.Value.TryGetProperty("address", out var addr) &&
                addr.GetString() == agentAddress)
            {
                lock (CacheGate)
                {
                    DirectInfoCache[agentAddress] = new DirectInfoCacheEntry(httpUrl, DateTimeOffset.UtcNow);
                }
                var directInfo = ParseInfoSource(info.Value);
                return MergeAgentInfo(fallback, profileInfo, directInfo, online: true);
            }
        }

        return fallback;
    }

    private static AgentInfo ParseInfoSource(JsonElement source)
    {
        string? name = null;
        if (TryGetString(source, "name", out var n) && !string.IsNullOrWhiteSpace(n)) name = n;
        else if (TryGetString(source, "alias", out var a) && !string.IsNullOrWhiteSpace(a)) name = a;

        return new AgentInfo(
            Address: "",
            Online: false,
            Name: name,
            Tools: NormalizeTools(source),
            Skills: NormalizeSkills(source),
            Trust: TryGetString(source, "trust", out var trust) ? trust : null,
            Version: TryGetString(source, "version", out var version) ? version : null,
            Model: TryGetString(source, "model", out var model) ? model : null,
            AcceptedInputs: ParseAcceptedInputs(source));
    }

    private static AgentInfo MergeAgentInfo(
        AgentInfo fallback, AgentInfo? profile, AgentInfo? direct, bool online)
    {
        return new AgentInfo(
            Address: fallback.Address,
            Online: online,
            Name: direct?.Name ?? profile?.Name ?? fallback.Name,
            Tools: direct?.Tools ?? profile?.Tools ?? fallback.Tools,
            Skills: direct?.Skills ?? profile?.Skills ?? fallback.Skills,
            Trust: direct?.Trust ?? profile?.Trust ?? fallback.Trust,
            Version: direct?.Version ?? profile?.Version ?? fallback.Version,
            Model: direct?.Model ?? profile?.Model ?? fallback.Model,
            AcceptedInputs: direct?.AcceptedInputs ?? profile?.AcceptedInputs ?? fallback.AcceptedInputs);
    }

    private static AgentInfo BuildAgentInfo(string address, bool online, JsonElement record)
    {
        if (record.TryGetProperty("profile", out var profile) &&
            profile.ValueKind == JsonValueKind.Object)
        {
            var parsed = ParseInfoSource(profile);
            return parsed with { Address = address, Online = online };
        }
        return new AgentInfo(address, online);
    }

    private static IReadOnlyList<string>? NormalizeTools(JsonElement source)
    {
        if (!source.TryGetProperty("tools", out var toolsEl) ||
            toolsEl.ValueKind != JsonValueKind.Array)
            return null;

        var names = new List<string>();
        foreach (var item in toolsEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) names.Add(s!);
            }
            else if (item.ValueKind == JsonValueKind.Object &&
                     item.TryGetProperty("name", out var nameEl) &&
                     nameEl.ValueKind == JsonValueKind.String)
            {
                var s = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(s)) names.Add(s!);
            }
        }
        return names.Count > 0 ? names : null;
    }

    private static IReadOnlyList<SkillInfo>? NormalizeSkills(JsonElement source)
    {
        if (!source.TryGetProperty("skills", out var skillsEl) ||
            skillsEl.ValueKind != JsonValueKind.Array)
            return null;

        var skills = new List<SkillInfo>();
        foreach (var item in skillsEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("name", out var nameEl) ||
                nameEl.ValueKind != JsonValueKind.String)
                continue;
            var name = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var desc = item.TryGetProperty("description", out var descEl) &&
                       descEl.ValueKind == JsonValueKind.String
                ? descEl.GetString() ?? ""
                : "";

            string? location = null;
            if (item.TryGetProperty("location", out var locEl) &&
                locEl.ValueKind == JsonValueKind.String)
            {
                var loc = locEl.GetString();
                if (!string.IsNullOrWhiteSpace(loc)) location = loc;
            }

            skills.Add(new SkillInfo(name!, desc, location));
        }
        return skills.Count > 0 ? skills : null;
    }

    /// <summary>
    /// Parses <c>accepted_inputs</c> straight out of a raw <c>/info</c> JSON
    /// response — works for both the direct-URL path (the agent's own <c>/info</c>
    /// verbatim) and the relay-composed JSON built by
    /// <c>AgentInfoService.FetchFromRelayAsync</c>, since both put
    /// <c>accepted_inputs</c> at the document root. Returns null on any parse
    /// failure rather than throwing — this feeds UI capability gating, not a
    /// hard requirement.
    /// </summary>
    public static AgentAcceptedInputs? ParseAcceptedInputsFromInfoJson(string infoJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(infoJson, JsonOptions);
            return ParseAcceptedInputs(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static AgentAcceptedInputs? ParseAcceptedInputs(JsonElement source)
    {
        if (!source.TryGetProperty("accepted_inputs", out var inputsEl) ||
            inputsEl.ValueKind != JsonValueKind.Object)
            return null;

        bool? text = null, images = null;
        AgentFileInputs? files = null;

        if (inputsEl.TryGetProperty("text", out var textEl) &&
            textEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            text = textEl.ValueKind == JsonValueKind.True;

        if (inputsEl.TryGetProperty("images", out var imagesEl) &&
            imagesEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            images = imagesEl.ValueKind == JsonValueKind.True;

        if (inputsEl.TryGetProperty("files", out var filesEl) &&
            filesEl.ValueKind == JsonValueKind.Object)
        {
            var maxSize = 0;
            var maxCount = 0;
            if (filesEl.TryGetProperty("max_file_size_mb", out var sizeEl) &&
                sizeEl.ValueKind == JsonValueKind.Number)
                maxSize = (int)sizeEl.GetDouble();
            if (filesEl.TryGetProperty("max_files_per_request", out var countEl) &&
                countEl.ValueKind == JsonValueKind.Number)
                maxCount = (int)countEl.GetDouble();
            if (maxSize > 0 || maxCount > 0)
                files = new AgentFileInputs(maxSize, maxCount);
        }

        if (text is null && images is null && files is null) return null;
        return new AgentAcceptedInputs(text, images, files);
    }

    private static bool TryGetString(JsonElement el, string key, out string? value)
    {
        if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
        {
            value = v.GetString();
            return true;
        }
        value = null;
        return false;
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
