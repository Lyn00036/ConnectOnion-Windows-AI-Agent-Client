using System;
using System.Collections.Generic;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services;

[Flags]
public enum AgentEndpointDuplicate
{
    None = 0,
    Address = 1,
    DirectUrl = 2,
}

/// <summary>
/// Compares the connection targets entered by the user with saved agents.
/// Direct URLs are compared by the effective base endpoint used by the
/// connection test, which ignores query strings and fragments.
/// </summary>
public static class AgentEndpointDuplicateDetector
{
    public static AgentEndpointDuplicate Find(
        IEnumerable<AgentConfig> agents,
        string? address,
        string? directUrl)
    {
        ArgumentNullException.ThrowIfNull(agents);

        var candidateAddress = address?.Trim();
        var candidateUrl = NormalizeDirectUrl(directUrl);
        var duplicate = AgentEndpointDuplicate.None;

        foreach (var agent in agents)
        {
            if (!string.IsNullOrEmpty(candidateAddress) &&
                string.Equals(candidateAddress, agent.Address?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                duplicate |= AgentEndpointDuplicate.Address;
            }

            if (!string.IsNullOrEmpty(candidateUrl) &&
                string.Equals(candidateUrl, NormalizeDirectUrl(agent.DirectUrl), StringComparison.Ordinal))
            {
                duplicate |= AgentEndpointDuplicate.DirectUrl;
            }

            if (duplicate == (AgentEndpointDuplicate.Address | AgentEndpointDuplicate.DirectUrl))
            {
                break;
            }
        }

        return duplicate;
    }

    private static string NormalizeDirectUrl(string? directUrl)
    {
        var value = directUrl?.Trim();
        if (string.IsNullOrEmpty(value)) return "";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value.TrimEnd('/').ToUpperInvariant();
        }

        var origin = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        var path = uri.AbsolutePath.TrimEnd('/');
        return origin + path;
    }
}
