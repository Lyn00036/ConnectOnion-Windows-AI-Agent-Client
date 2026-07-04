using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services;

public readonly record struct ConnectionTestResult(
    bool Ok, string Detail, string? AgentName = null,
    bool InfoChanged = false, string? InfoJson = null);

/// <summary>
/// Reachability check for an agent target. Port of <c>connectionTest.ts</c>.
///
/// Direct URL agents expose an unauthenticated <c>GET /health</c> route and are
/// probed here. Relay-address agents require the SDK's endpoint resolution and
/// are handled once the protocol layer lands (Step 9).
/// </summary>
public static class ConnectionTester
{
    private const int DefaultTimeoutMs = 6000;
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowDuplicateProperties = false,
    };
    private static readonly Regex AddressPattern = new("^[a-zA-Z0-9]+$", RegexOptions.Compiled);

    public static async Task<ConnectionTestResult> TestAsync(
        string? address, string? directUrl, int timeoutMs = DefaultTimeoutMs,
        AgentConfig? existingAgent = null)
    {
        directUrl = directUrl?.Trim();

        if (!string.IsNullOrEmpty(directUrl))
        {
            // A Direct URL is the most specific target, so prefer it over a relay
            // address when both are present.
            if (!TryBuildHealthUrl(directUrl, out var healthUrl))
            {
                return new ConnectionTestResult(false, "Invalid Direct URL.");
            }

            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                using var response = await AppServices.Http
                    .GetAsync(healthUrl, cts.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return new ConnectionTestResult(false, $"Agent responded with HTTP {(int)response.StatusCode}.");
                }

                var name = await ReadHealthAgentNameAsync(response).ConfigureAwait(false);
                var suffix = name is null ? "" : $" - {name}";

                // If we have a stored agent, check whether its /info changed.
                var infoChanged = false;
                string? infoJson = null;
                if (existingAgent is not null)
                {
                    (infoChanged, infoJson) = await AgentInfoService.CheckForChangesAsync(existingAgent);
                }

                return new ConnectionTestResult(
                    true, $"Reachable via Direct URL{suffix}.", AgentName: name,
                    InfoChanged: infoChanged, InfoJson: infoJson);
            }
            catch (OperationCanceledException)
            {
                return new ConnectionTestResult(false, "Connection timed out - the agent did not respond in time.");
            }
            catch
            {
                return new ConnectionTestResult(false, "Could not reach the agent (network error or invalid URL).");
            }
        }

        address = address?.Trim();
        if (!string.IsNullOrEmpty(address))
        {
            if (!AddressPattern.IsMatch(address))
            {
                return new ConnectionTestResult(false, "Agent address can only contain letters and numbers.");
            }

            try
            {
                var info = await ConnectOnion.Protocol.EndpointResolver
                    .FetchAgentInfoAsync(AppServices.Http, address, ConnectOnion.Protocol.EndpointResolver.DefaultRelay)
                    .ConfigureAwait(false);
                if (info.Online)
                {
                    var suffix = string.IsNullOrEmpty(info.Name) ? "" : $" - {info.Name}";
                    return new ConnectionTestResult(true, $"Online via relay{suffix}.", AgentName: info.Name);
                }
                return new ConnectionTestResult(false, "Registered on the relay but currently offline.");
            }
            catch
            {
                return new ConnectionTestResult(false, "Could not reach the relay.");
            }
        }

        return new ConnectionTestResult(false, "Enter a Direct URL or agent address first.");
    }

    // Preserve the configured origin/base path, append /health, drop any
    // user-entered query string or fragment.
    private static bool TryBuildHealthUrl(string directUrl, out Uri healthUrl)
    {
        healthUrl = null!;
        if (!Uri.TryCreate(directUrl, UriKind.Absolute, out var uri)) return false;

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/health",
            Query = string.Empty,
            Fragment = string.Empty,
        };
        healthUrl = builder.Uri;
        return true;
    }

    private static async Task<string?> ReadHealthAgentNameAsync(HttpResponseMessage response)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, JsonOptions).ConfigureAwait(false);
            return ExtractAgentName(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    // Different SDK/server versions exposed the display name under slightly
    // different keys and a nested `agent` object; read the known spellings.
    private static string? ExtractAgentName(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var s = value.GetString()?.Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        if (value.ValueKind != JsonValueKind.Object) return null;

        foreach (var key in new[] { "name", "agentName", "agent_name" })
        {
            if (value.TryGetProperty(key, out var field) &&
                field.ValueKind == JsonValueKind.String)
            {
                var s = field.GetString()?.Trim();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }

        return value.TryGetProperty("agent", out var nested) ? ExtractAgentName(nested) : null;
    }
}
