using System.Text.Json.Serialization;
using ConnectOnion.WinUIClient.Models;
using Microsoft.Data.Sqlite;

namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Persisted shape of saved agents: the agent list plus the selected id. SQLite
/// stores the list as rows and the selected pointer in app metadata.
///
/// Load/mutate/Save as a whole document — callers add or remove from
/// <see cref="Agents"/> and hand the object back. That is affordable because the list is
/// small and user-sized; the conversation tables, which are not, use incremental writes
/// instead (see <c>ConversationRepository</c>).
/// </summary>
public sealed class AgentsState
{
    [JsonPropertyName("agents")]
    public List<AgentConfig> Agents { get; set; } = new();

    [JsonPropertyName("selectedAgentId")]
    public string? SelectedAgentId { get; set; }
}

/// <summary>
/// Local SQLite persistence for saved agents. Port of <c>agentStorage.ts</c>.
/// </summary>
public sealed class AgentRepository
{
    private const string SelectedAgentMetaKey = "selected_agent_id";

    /// <summary>Updates only the selected-agent pointer without reconciling every agent row.</summary>
    public async Task SetSelectedAgentAsync(
        string? agentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await AppDatabase.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(agentId))
            await AppDatabase.DeleteMetaAsync(connection, null, SelectedAgentMetaKey).ConfigureAwait(false);
        else
            await AppDatabase.SetMetaAsync(connection, null, SelectedAgentMetaKey, agentId).ConfigureAwait(false);
        StorageRevision.Bump();
    }

    /// <summary>
    /// Loads the agent list and selected id. Invalid rows are dropped (name
    /// required, the reserved "local-agent" id excluded, address required) and a
    /// stale selected pointer left after a deletion is cleared — mirroring the
    /// web app's defensive read.
    /// </summary>
    public async Task<AgentsState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await AppDatabase.OpenAsync(cancellationToken).ConfigureAwait(false);
        var state = new AgentsState
        {
            SelectedAgentId = await AppDatabase.GetMetaAsync(connection, SelectedAgentMetaKey).ConfigureAwait(false),
        };

        await using (var command = connection.CreateCommand())
        {
            // sort_order is the user's arrangement; name and id are tie-breakers so the list
            // is *stable* rather than in whatever order SQLite returns rows. Without them,
            // agents sharing a sort_order (everything, before the first reorder — they all
            // default to 0) could shuffle between launches. COLLATE NOCASE so the fallback
            // ordering is alphabetical the way a human reads it.
            command.CommandText = """
                SELECT id, name, address, direct_url, icon_path, invite_code, info_json, info_updated_at
                FROM agents
                ORDER BY sort_order, name COLLATE NOCASE, id;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                state.Agents.Add(new AgentConfig
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Address = reader.GetString(2),
                    DirectUrl = ReadNullableString(reader, 3),
                    IconPath = ReadNullableString(reader, 4),
                    InviteCode = ReadNullableString(reader, 5),
                    InfoJson = ReadNullableString(reader, 6),
                    InfoUpdatedAt = ReadNullableString(reader, 7),
                });
            }
        }

        // Filtered on read as well as on write. The write-side check below should make this
        // redundant, but a database can predate that check or have been edited by hand, and
        // an agent with no name or no endpoint renders as an unusable blank row that the user
        // cannot even select to delete. Dropping it from the read is the recoverable outcome.
        // "local-agent" is a reserved id from the web client that never applies here.
        state.Agents = state.Agents
            .Where(a => !string.IsNullOrWhiteSpace(a.Name)
                        && a.Id != "local-agent"
                        && HasConnectionTarget(a))
            .ToList();

        // The pointer lives in app_meta with no FK to enforce it, so deleting an agent can
        // leave it dangling. Clearing it here means every caller sees "nothing selected"
        // rather than a selected id that matches no row.
        if (state.SelectedAgentId is not null &&
            state.Agents.All(a => a.Id != state.SelectedAgentId))
        {
            state.SelectedAgentId = null;
        }

        return state;
    }

    /// <summary>
    /// Reconciles the whole agent list. Existing rows are upserted in the caller's order, then
    /// omitted agents are removed after their complete conversation graph has been deleted.
    /// This ordering keeps <c>sessions.agent_id</c> valid throughout the transaction and makes
    /// repository-level deletion safe even outside the shell's coordinated runtime cleanup.
    /// </summary>
    public async Task SaveAsync(AgentsState state, CancellationToken cancellationToken = default)
    {
        await using var connection = await AppDatabase.OpenAsync(cancellationToken).ConfigureAwait(false);
        // One transaction over reconciliation, graph cleanup, and the selected-id write: a
        // crash mid-save must not be able to leave a partially updated agent/session graph.
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var validAgents = state.Agents
            .Where(agent => !string.IsNullOrWhiteSpace(agent.Id)
                            && !string.IsNullOrWhiteSpace(agent.Name)
                            && HasConnectionTarget(agent)
                            && agent.Id != "local-agent")
            .ToList();

        for (var index = 0; index < validAgents.Count; index++)
        {
            var agent = validAgents[index];

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO agents (id, name, address, direct_url, icon_path, invite_code, info_json, info_updated_at, sort_order)
                VALUES ($id, $name, $address, $direct_url, $icon_path, $invite_code, $info_json, $info_updated_at, $sort_order)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    address = excluded.address,
                    direct_url = excluded.direct_url,
                    icon_path = excluded.icon_path,
                    invite_code = excluded.invite_code,
                    info_json = excluded.info_json,
                    info_updated_at = excluded.info_updated_at,
                    sort_order = excluded.sort_order;
                """;
            AppDatabase.Add(insert, "$id", agent.Id);
            AppDatabase.Add(insert, "$name", agent.Name);
            AppDatabase.Add(insert, "$address", agent.Address);
            AppDatabase.Add(insert, "$direct_url", agent.DirectUrl);
            // Blank and null both mean "no custom icon"; store the one representation so a
            // caller that cleared the path can never leave an empty string the UI would resolve.
            AppDatabase.Add(insert, "$icon_path",
                string.IsNullOrWhiteSpace(agent.IconPath) ? null : agent.IconPath);
            AppDatabase.Add(insert, "$invite_code", agent.InviteCode);
            AppDatabase.Add(insert, "$info_json", agent.InfoJson);
            AppDatabase.Add(insert, "$info_updated_at", agent.InfoUpdatedAt);
            // Position in the caller's list *is* the persisted order — reordering the sidebar
            // is just saving the list in its new order, with no explicit reorder API.
            AppDatabase.Add(insert, "$sort_order", index);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await DeleteRemovedAgentGraphsAsync(
                connection,
                transaction,
                validAgents.Select(agent => agent.Id).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);

        // Delete rather than store an empty string, so "no selection" has exactly one
        // representation (a missing key) for GetMetaAsync's null to mean.
        if (string.IsNullOrWhiteSpace(state.SelectedAgentId))
        {
            await AppDatabase.DeleteMetaAsync(connection, transaction, SelectedAgentMetaKey)
                .ConfigureAwait(false);
        }
        else
        {
            await AppDatabase.SetMetaAsync(connection, transaction, SelectedAgentMetaKey, state.SelectedAgentId!)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        StorageRevision.Bump();
    }

    private static async Task DeleteRemovedAgentGraphsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string> keepAgentIds,
        CancellationToken cancellationToken)
    {
        var sessionPredicate = keepAgentIds.Count == 0
            ? "1 = 1"
            : $"agent_id NOT IN ({string.Join(", ", keepAgentIds.Select((_, index) => $"$keep{index}"))})";
        var agentPredicate = keepAgentIds.Count == 0
            ? "1 = 1"
            : $"id NOT IN ({string.Join(", ", keepAgentIds.Select((_, index) => $"$keep{index}"))})";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            DELETE FROM message_attachments
            WHERE conversation_id IN (SELECT id FROM sessions WHERE {sessionPredicate});

            DELETE FROM messages
            WHERE conversation_id IN (SELECT id FROM sessions WHERE {sessionPredicate});

            DELETE FROM trace_events
            WHERE conversation_id IN (SELECT id FROM sessions WHERE {sessionPredicate});

            DELETE FROM executions
            WHERE conversation_id IN (SELECT id FROM sessions WHERE {sessionPredicate});

            DELETE FROM sessions WHERE {sessionPredicate};
            DELETE FROM agents WHERE {agentPredicate};
            """;
        for (var index = 0; index < keepAgentIds.Count; index++)
        {
            AppDatabase.Add(command, $"$keep{index}", keepAgentIds[index]);
        }
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>An agent is only usable if it can be reached somehow — by relay address or by
    /// direct URL. Either alone is enough; neither makes the row dead weight, which is why
    /// this gates both the read and the write.</summary>
    private static bool HasConnectionTarget(AgentConfig agent)
        => !string.IsNullOrWhiteSpace(agent.Address) ||
           !string.IsNullOrWhiteSpace(agent.DirectUrl);
}
