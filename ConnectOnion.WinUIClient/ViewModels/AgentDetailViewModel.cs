using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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
public sealed class AgentDetailViewModel : PresenceAwareViewModel
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private AgentConfig? _agent;
    private bool _isLoading;

    // Bumped by every LoadAsync. The page is reused across agents (see Views.IReloadablePage),
    // so loads can overlap; only the newest may write to it.
    private long _loadGeneration;
    private string _infoJson = "";
    private string _statusText = "";
    private bool _hasError;
    private bool _enterToSend = true;

    /// <summary>The agent this page tracks for presence (base class reads this).</summary>
    protected override AgentConfig? PresenceAgent => _agent;

    /// <summary>Detaches from the shared presence service. Call when the page unloads.</summary>
    public void Cleanup() => DetachPresence();

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
    public string AgentInitial => NameInitial.From(AgentName);
    public string AgentAddress => _agent?.Address ?? "";
    public string? DirectUrl => _agent?.DirectUrl;
    public bool HasAgent => _agent is not null;

    public bool CanStartConversation => HasAgent && IsOnline;

    /// <summary>Also refresh whether a conversation can be started (gated on online).</summary>
    protected override void OnPresenceRaised()
        => OnPropertyChanged(nameof(CanStartConversation));

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

    public bool EnterToSend { get => _enterToSend; private set => SetProperty(ref _enterToSend, value); }

    private AgentAcceptedInputs? _acceptedInputs;
    /// <summary>Same purpose as <c>ChatViewModel.AcceptedInputs</c>: gates the composer's attachment UI against the agent's real <c>/info</c> capability.</summary>
    public AgentAcceptedInputs? AcceptedInputs
    {
        get => _acceptedInputs;
        private set => SetProperty(ref _acceptedInputs, value);
    }

    /// <summary>
    /// Starts a new conversation. <paramref name="hasAttachments"/> must be true
    /// when the composer submission carries attachments even if
    /// <paramref name="prompt"/> is empty — an image/file-only send is valid and
    /// must still create and navigate to a session, matching ChatPage's own
    /// composer (previously this only checked the text, so an attachment-only
    /// submission here silently did nothing).
    /// </summary>
    public async Task<bool> StartConversationAsync(string prompt, bool hasAttachments = false)
    {
        if (Agent is null || !IsOnline || (string.IsNullOrWhiteSpace(prompt) && !hasAttachments)) return false;

        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = Agent.Id;
        await AppServices.Agents.SaveAsync(agentsState);

        var sessionsState = await AppServices.Sessions.LoadAsync();
        var session = SessionSummary.NewConversation(Agent.Id, sessionsState.Sessions);
        sessionsState.Sessions.Add(session);
        sessionsState.ActiveSessionId = session.Id;
        await AppServices.Sessions.SaveAsync(sessionsState);
        return true;
    }

    /// <summary>Loads agent info from cache or fetches fresh data.</summary>
    public async Task LoadAsync()
    {
        // Switching agents reuses this page rather than rebuilding it, so two loads can be in
        // flight at once — and an /info fetch is slow enough to outlive the agent that asked for
        // it. Only the newest load may write to the page; a superseded one drops out here, and
        // its fetch drops out where it would otherwise publish (FetchInfoWithCacheAsync).
        var generation = ++_loadGeneration;

        var prefs = await AppServices.Preferences.LoadAsync();
        if (generation != _loadGeneration) return;
        EnterToSend = prefs.EnterToSend;

        var agents = await AppServices.Agents.LoadAsync();
        if (generation != _loadGeneration) return;

        var selected = agents.Agents.FirstOrDefault(a => a.Id == agents.SelectedAgentId);

        Agent = selected;
        if (Agent is null) return;

        // Probe once on open; the offline bar stays hidden until it resolves so a
        // stale cached "offline" never flashes the reconnect UI. Placed before the
        // /info early-returns below so it always runs.
        _ = ProbePresenceOnOpenAsync();

        // Use cached /info immediately; only fetch when the cache is missing or stale.
        if (!string.IsNullOrWhiteSpace(Agent.InfoJson))
        {
            PopulateFromCachedJson(Agent.InfoJson);
            StatusText = $"Cached — {LastUpdated}";
            if (AgentInfoService.IsCacheFresh(Agent)) return;
        }

        IsLoading = true;
        await FetchInfoWithCacheAsync(generation);
    }

    private async Task FetchInfoWithCacheAsync(long generation)
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
            // The page has moved on to another agent; publishing now would show one agent's info
            // under another's name. IsLoading belongs to the newer load, so don't clear it either.
            if (generation != _loadGeneration) return;

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

        AcceptedInputs = EndpointResolver.ParseAcceptedInputsFromInfoJson(json);
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
