using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Dispatching;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// Backs the Agent Detail page: loads the selected agent's /info payload and
/// exposes key fields plus the raw JSON for the UI.
/// </summary>
public sealed class AgentDetailViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly AgentPresenceService _presence = AppServices.Presence;
    private AgentConfig? _agent;
    private bool _isLoading;
    private string _infoJson = "";
    private string _statusText = "";
    private bool _hasError;

    public AgentDetailViewModel()
    {
        _presence.PresenceChanged += OnPresenceChanged;
    }

    /// <summary>Detaches from the shared presence service. Call when the page unloads.</summary>
    public void Cleanup() => _presence.PresenceChanged -= OnPresenceChanged;

    private void OnPresenceChanged(string agentId)
    {
        if (_agent is null || agentId != _agent.Id) return;
        _dispatcher.TryEnqueue(RaisePresenceProperties);
    }

    public AgentConfig? Agent
    {
        get => _agent;
        private set
        {
            if (!SetProperty(ref _agent, value)) return;
            OnPropertyChanged(nameof(AgentName));
            OnPropertyChanged(nameof(AgentAddress));
            OnPropertyChanged(nameof(DirectUrl));
            OnPropertyChanged(nameof(HasAgent));
            OnPropertyChanged(nameof(AgentInitial));
            RaisePresenceProperties();
            RaiseProfileSummaryProperties();
        }
    }

    public string AgentName => _agent?.Name ?? "";
    public string AgentInitial => string.IsNullOrWhiteSpace(AgentName) ? "?" : AgentName[..1].ToUpperInvariant();
    public string AgentAddress => _agent?.Address ?? "";
    public string? DirectUrl => _agent?.DirectUrl;
    public bool HasAgent => _agent is not null;

    public AgentPresence Presence => _agent is null ? AgentPresence.Unknown : _presence.GetPresence(_agent.Id);
    public bool IsOnline => Presence == AgentPresence.Online;
    public string PresenceLabel => Presence switch
    {
        AgentPresence.Online => "Online",
        AgentPresence.Checking => "Checking...",
        AgentPresence.Offline => "Offline",
        _ => "Unknown",
    };

    private void RaisePresenceProperties()
    {
        OnPropertyChanged(nameof(Presence));
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(PresenceLabel));
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string InfoJson { get => _infoJson; private set { if (SetProperty(ref _infoJson, value)) OnPropertyChanged(nameof(HasInfoJson)); } }

    public bool HasInfoJson => !string.IsNullOrWhiteSpace(InfoJson);

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }

    public ObservableCollection<InfoField> InfoFields { get; } = new();

    public string DisplayModel => GetInfoValue("model") ?? "deepseek-v4-pro";

    public bool HasToolSummary => !string.IsNullOrWhiteSpace(ToolSummaryLineOne);

    public string ToolSummaryLineOne
    {
        get
        {
            var tools = GetToolNames().Take(5).Select(FormatToolName).ToList();
            return tools.Count == 0 ? "" : string.Join(" · ", tools) + " ·";
        }
    }

    public string ToolSummaryLineTwo
    {
        get
        {
            var tools = GetToolNames().Skip(5).Select(FormatToolName).ToList();
            if (tools.Count == 0) return "";

            var line = string.Join(" · ", tools.Take(3));
            var remaining = tools.Count - 3;
            return remaining > 0 ? $"{line} +{remaining} more" : line;
        }
    }

    public string ShortAddress
        => string.IsNullOrWhiteSpace(AgentAddress)
            ? ""
            : AgentAddress.Length >= 10
                ? $"{AgentAddress[..6]}...{AgentAddress[^4..]}"
                : AgentAddress;

    public string? LastUpdated => Agent?.InfoUpdatedAt is { } ts
        ? $"Last updated {ts}"
        : null;

    public async Task<bool> StartConversationAsync(string prompt)
    {
        if (Agent is null || string.IsNullOrWhiteSpace(prompt)) return false;

        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = Agent.Id;
        await AppServices.Agents.SaveAsync(agentsState);

        var sessionsState = await AppServices.Sessions.LoadAsync();
        var timestamp = DateTime.UtcNow.ToString("o");
        var count = sessionsState.Sessions.Count(s => s.AgentId == Agent.Id) + 1;
        sessionsState.Sessions.Add(new SessionSummary
        {
            Id = Guid.NewGuid().ToString(),
            AgentId = Agent.Id,
            Title = $"Conversation {count}",
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        });
        sessionsState.ActiveSessionId = sessionsState.Sessions[^1].Id;
        await AppServices.Sessions.SaveAsync(sessionsState);
        return true;
    }

    /// <summary>Loads agent info from cache or fetches fresh data.</summary>
    public async Task LoadAsync()
    {
        var agents = await AppServices.Agents.LoadAsync();
        var selected = agents.Agents.FirstOrDefault(a => a.Id == agents.SelectedAgentId);

        Agent = selected;
        if (Agent is null) return;

        // Use cached /info immediately; only fetch when the cache is missing or stale.
        if (!string.IsNullOrWhiteSpace(Agent.InfoJson))
        {
            PopulateFromCachedJson(Agent.InfoJson);
            StatusText = $"Cached — {LastUpdated}";
            if (AgentInfoService.IsCacheFresh(Agent)) return;
        }

        IsLoading = true;
        await FetchInfoWithCacheAsync();
    }

    private async Task FetchInfoWithCacheAsync()
    {
        var agent = Agent;
        if (agent is null)
        {
            IsLoading = false;
            return;
        }

        var hadCache = !string.IsNullOrWhiteSpace(agent.InfoJson);
        var freshJson = await AgentInfoService.FetchAndPersistAsync(agent, forceRefresh: true);

        _dispatcher.TryEnqueue(() =>
        {
            if (!string.IsNullOrWhiteSpace(freshJson))
            {
                HasError = false;
                PopulateFromCachedJson(freshJson);
                OnPropertyChanged(nameof(LastUpdated));
                StatusText = $"Updated - {LastUpdated}";
            }
            else if (hadCache)
            {
                HasError = false;
                StatusText = $"Cached - {LastUpdated}";
            }
            else
            {
                InfoFields.Clear();
                InfoJson = "";
                HasError = true;
                StatusText = "Could not load agent info.";
            }

            IsLoading = false;
        });
    }

    private async Task FetchInfoAsync()
    {
        var directUrl = Agent?.DirectUrl;
        var address = Agent?.Address;

        if (!string.IsNullOrWhiteSpace(directUrl))
        {
            string? json = null;
            string? error = null;

            try
            {
                var infoUrl = directUrl.TrimEnd('/') + "/info";
                using var cts = new System.Threading.CancellationTokenSource(8000);
                using var response = await AppServices.Http.GetAsync(infoUrl, cts.Token);
                if (response.IsSuccessStatusCode)
                    json = await response.Content.ReadAsStringAsync(cts.Token);
                else
                    error = $"Agent returned HTTP {(int)response.StatusCode}.";
            }
            catch (OperationCanceledException)
            {
                error = "Request timed out — the agent did not respond.";
            }
            catch (HttpRequestException ex)
            {
                error = $"Could not reach agent: {ex.Message}";
            }
            catch (JsonException)
            {
                error = "Agent returned invalid JSON.";
            }
            catch (Exception ex)
            {
                error = $"Unexpected error: {ex.Message}";
            }

            _dispatcher.TryEnqueue(() =>
            {
                InfoFields.Clear();
                InfoJson = "";
                if (json is not null)
                {
                    PopulateFromCachedJson(json);
                    StatusText = $"Connected — {InfoFields.Count} fields loaded.";
                }
                else
                {
                    HasError = true;
                    StatusText = error ?? "Unknown error.";
                }
                IsLoading = false;
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(address))
        {
            AgentInfo? info = null;
            string? error = null;

            try
            {
                info = await EndpointResolver.FetchAgentInfoAsync(
                    AppServices.Http, address, EndpointResolver.DefaultRelay);
            }
            catch (Exception ex)
            {
                error = $"Relay lookup failed: {ex.Message}";
            }

            _dispatcher.TryEnqueue(() =>
            {
                InfoFields.Clear();
                InfoJson = "";
                if (info is not null)
                {
                    PopulateFromAgentInfo(info);
                    StatusText = info.Online
                        ? $"Connected via relay — {InfoFields.Count} fields loaded."
                        : "Agent is registered on the relay but currently offline.";
                    HasError = !info.Online;
                }
                else
                {
                    HasError = true;
                    StatusText = error ?? "Unknown error.";
                }
                IsLoading = false;
            });
            return;
        }

        _dispatcher.TryEnqueue(() =>
        {
            StatusText = "No endpoint configured for this agent.";
            IsLoading = false;
        });
    }

    /// <summary>
    /// Populates <see cref="InfoFields"/> from the structured <see cref="AgentInfo"/>
    /// record, matching the same key names that the /info JSON would produce.
    /// </summary>
    private void PopulateFromAgentInfo(AgentInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.Name))
            InfoFields.Add(new InfoField("name", info.Name));
        InfoFields.Add(new InfoField("address", info.Address));

        if (info.Tools is { Count: > 0 })
            InfoFields.Add(new InfoField("tools", string.Join(", ", info.Tools)));

        if (info.Skills is { Count: > 0 })
        {
            for (var i = 0; i < info.Skills.Count; i++)
            {
                var skill = info.Skills[i];
                InfoFields.Add(new InfoField($"skills[{i}].name", skill.Name));
                if (!string.IsNullOrWhiteSpace(skill.Description))
                    InfoFields.Add(new InfoField($"skills[{i}].description", skill.Description));
                if (!string.IsNullOrWhiteSpace(skill.Location))
                    InfoFields.Add(new InfoField($"skills[{i}].location", skill.Location));
            }
        }

        if (!string.IsNullOrWhiteSpace(info.Trust))
            InfoFields.Add(new InfoField("trust", info.Trust));
        if (!string.IsNullOrWhiteSpace(info.Version))
            InfoFields.Add(new InfoField("version", info.Version));
        if (!string.IsNullOrWhiteSpace(info.Model))
            InfoFields.Add(new InfoField("model", info.Model));
        InfoFields.Add(new InfoField("online", info.Online ? "true" : "false"));

        if (info.AcceptedInputs is { } ai)
        {
            if (ai.Text.HasValue)
                InfoFields.Add(new InfoField("accepted_inputs.text", ai.Text.Value ? "true" : "false"));
            if (ai.Images.HasValue)
                InfoFields.Add(new InfoField("accepted_inputs.images", ai.Images.Value ? "true" : "false"));
            if (ai.Files is { } f)
            {
                InfoFields.Add(new InfoField("accepted_inputs.files.max_file_size_mb", f.MaxFileSizeMb.ToString()));
                InfoFields.Add(new InfoField("accepted_inputs.files.max_files_per_request", f.MaxFilesPerRequest.ToString()));
            }
        }
        RaiseProfileSummaryProperties();
    }

    /// <summary>
    /// Populates <see cref="InfoFields"/> from a cached JSON string,
    /// using the same recursive flattening as the Direct URL path.
    /// </summary>
    private void PopulateFromCachedJson(string json)
    {
        InfoFields.Clear();
        InfoJson = json;
        try
        {
            using var doc = JsonDocument.Parse(json);
            FlattenObject(doc.RootElement, "", InfoFields);
            RaiseProfileSummaryProperties();
        }
        catch
        {
            // Corrupt cache: leave fields empty, a refresh will fix it.
        }
    }

    /// <summary>
    /// Recursively flattens nested objects/arrays into a flat key-value list.
    /// Nested keys use dot notation: <c>accepted_inputs.files.max_file_size_mb</c>.
    /// Arrays of simple values are comma-joined; arrays of objects are expanded
    /// with index prefixes.
    /// </summary>
    private static void FlattenObject(
        JsonElement element,
        string prefix,
        ObservableCollection<InfoField> fields)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    var value = prop.Value;
                    if (value.ValueKind is JsonValueKind.Object
                        or JsonValueKind.Array)
                    {
                        FlattenObject(value, key, fields);
                    }
                    else
                    {
                        fields.Add(new InfoField(key, ElementToString(value)));
                    }
                }
                break;

            case JsonValueKind.Array:
                var items = element.EnumerateArray().ToList();
                if (items.Count == 0)
                {
                    fields.Add(new InfoField(prefix, "[empty]"));
                    return;
                }

                // If every element is a simple value, join them.
                if (items.All(it => it.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)))
                {
                    var joined = string.Join(", ", items.Select(ElementToString));
                    fields.Add(new InfoField(prefix, joined));
                    return;
                }

                // Mixed or object array: expand each item.
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var indexedPrefix = $"{prefix}[{i}]";
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        FlattenObject(item, indexedPrefix, fields);
                    }
                    else
                    {
                        fields.Add(new InfoField(indexedPrefix, ElementToString(item)));
                    }
                }
                break;
        }
    }

    private void RaiseProfileSummaryProperties()
    {
        OnPropertyChanged(nameof(DisplayModel));
        OnPropertyChanged(nameof(HasToolSummary));
        OnPropertyChanged(nameof(ToolSummaryLineOne));
        OnPropertyChanged(nameof(ToolSummaryLineTwo));
    }

    private string? GetInfoValue(string name)
        => InfoFields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private List<string> GetToolNames()
    {
        var direct = GetInfoValue("tools");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(tool => tool.Length > 0)
                .ToList();
        }

        return InfoFields
            .Where(field => field.Name.StartsWith("tools[", StringComparison.OrdinalIgnoreCase)
                            && (field.Name.EndsWith(".name", StringComparison.OrdinalIgnoreCase)
                                || !field.Name.Contains("].", StringComparison.OrdinalIgnoreCase)))
            .Select(field => field.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatToolName(string value)
    {
        var normalized = value.Replace('_', ' ').Replace('-', ' ').Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string ElementToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "—",
        _ => "—",
    };
}

/// <summary>A named field from the /info response payload.</summary>
public sealed record InfoField(string Name, string Value);
