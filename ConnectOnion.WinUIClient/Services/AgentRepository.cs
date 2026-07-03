using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using Microsoft.Data.Sqlite;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Persisted shape of saved agents: the agent list plus the selected id. SQLite
/// stores the list as rows and the selected pointer in app metadata.
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
    /// <summary>
    /// Loads the agent list and selected id. Invalid rows are dropped (name
    /// required, the reserved "local-agent" id excluded, address required) and a
    /// stale selected pointer left after a deletion is cleared — mirroring the
    /// web app's defensive read.
    /// </summary>
    public async Task<AgentsState> LoadAsync()
    {
        await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
        var state = new AgentsState
        {
            SelectedAgentId = await AppDatabase.GetMetaAsync(connection, "selected_agent_id").ConfigureAwait(false),
        };

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, name, address, direct_url, invite_code
                FROM agents
                ORDER BY sort_order, name COLLATE NOCASE, id;
                """;
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                state.Agents.Add(new AgentConfig
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Address = reader.GetString(2),
                    DirectUrl = ReadNullableString(reader, 3),
                    InviteCode = ReadNullableString(reader, 4),
                });
            }
        }

        state.Agents = state.Agents
            .Where(a => !string.IsNullOrWhiteSpace(a.Name)
                        && a.Id != "local-agent"
                        && !string.IsNullOrWhiteSpace(a.Address))
            .ToList();

        if (state.SelectedAgentId is not null &&
            state.Agents.All(a => a.Id != state.SelectedAgentId))
        {
            state.SelectedAgentId = null;
        }

        return state;
    }

    public async Task SaveAsync(AgentsState state)
    {
        await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM agents;";
            await delete.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        for (var index = 0; index < state.Agents.Count; index++)
        {
            var agent = state.Agents[index];
            if (string.IsNullOrWhiteSpace(agent.Id) ||
                string.IsNullOrWhiteSpace(agent.Name) ||
                string.IsNullOrWhiteSpace(agent.Address) ||
                agent.Id == "local-agent")
            {
                continue;
            }

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO agents (id, name, address, direct_url, invite_code, sort_order)
                VALUES ($id, $name, $address, $direct_url, $invite_code, $sort_order);
                """;
            AppDatabase.Add(insert, "$id", agent.Id);
            AppDatabase.Add(insert, "$name", agent.Name);
            AppDatabase.Add(insert, "$address", agent.Address);
            AppDatabase.Add(insert, "$direct_url", agent.DirectUrl);
            AppDatabase.Add(insert, "$invite_code", agent.InviteCode);
            AppDatabase.Add(insert, "$sort_order", index);
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(state.SelectedAgentId))
        {
            await AppDatabase.DeleteMetaAsync(connection, transaction, "selected_agent_id")
                .ConfigureAwait(false);
        }
        else
        {
            await AppDatabase.SetMetaAsync(connection, transaction, "selected_agent_id", state.SelectedAgentId!)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
