using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using Microsoft.Data.Sqlite;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Persisted shape of the session index plus the active id. SQLite stores the
/// list as rows and the active pointer in app metadata.
/// </summary>
public sealed class SessionsState
{
    [JsonPropertyName("sessions")]
    public List<SessionSummary> Sessions { get; set; } = new();

    [JsonPropertyName("activeSessionId")]
    public string? ActiveSessionId { get; set; }
}

/// <summary>
/// Local SQLite persistence for conversation index entries. Port of <c>sessionStorage.ts</c>.
/// </summary>
public sealed class SessionRepository
{
    /// <summary>
    /// Loads sessions, dropping partial/corrupt rows (id, agentId and title all
    /// required) and clearing a stale active pointer.
    /// </summary>
    public async Task<SessionsState> LoadAsync()
    {
        await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
        var state = new SessionsState
        {
            ActiveSessionId = await AppDatabase.GetMetaAsync(connection, "active_session_id").ConfigureAwait(false),
        };

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, agent_id, title, created_at, updated_at
                FROM sessions
                ORDER BY sort_order, updated_at DESC, id;
                """;
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                state.Sessions.Add(new SessionSummary
                {
                    Id = reader.GetString(0),
                    AgentId = reader.GetString(1),
                    Title = reader.GetString(2),
                    CreatedAt = reader.GetString(3),
                    UpdatedAt = reader.GetString(4),
                });
            }
        }

        state.Sessions = state.Sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.Id)
                        && !string.IsNullOrWhiteSpace(s.AgentId)
                        && !string.IsNullOrWhiteSpace(s.Title))
            .ToList();

        if (state.ActiveSessionId is not null &&
            state.Sessions.All(s => s.Id != state.ActiveSessionId))
        {
            state.ActiveSessionId = null;
        }

        return state;
    }

    public async Task SaveAsync(SessionsState state)
    {
        await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM sessions;";
            await delete.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        for (var index = 0; index < state.Sessions.Count; index++)
        {
            var session = state.Sessions[index];
            if (string.IsNullOrWhiteSpace(session.Id) ||
                string.IsNullOrWhiteSpace(session.AgentId) ||
                string.IsNullOrWhiteSpace(session.Title))
            {
                continue;
            }

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO sessions (id, agent_id, title, created_at, updated_at, sort_order)
                VALUES ($id, $agent_id, $title, $created_at, $updated_at, $sort_order);
                """;
            AppDatabase.Add(insert, "$id", session.Id);
            AppDatabase.Add(insert, "$agent_id", session.AgentId);
            AppDatabase.Add(insert, "$title", session.Title);
            AppDatabase.Add(insert, "$created_at", session.CreatedAt);
            AppDatabase.Add(insert, "$updated_at", session.UpdatedAt);
            AppDatabase.Add(insert, "$sort_order", index);
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(state.ActiveSessionId))
        {
            await AppDatabase.DeleteMetaAsync(connection, transaction, "active_session_id")
                .ConfigureAwait(false);
        }
        else
        {
            await AppDatabase.SetMetaAsync(connection, transaction, "active_session_id", state.ActiveSessionId!)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }
}
