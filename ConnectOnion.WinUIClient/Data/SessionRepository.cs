using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using Microsoft.Data.Sqlite;

namespace ConnectOnion.WinUIClient.Data;

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
    private const string ActiveSessionMetaKey = "active_session_id";
    private const string PinnedSessionsMetaKey = "pinned_session_ids";

    public event System.Action? SessionsChanged;

    /// <summary>
    /// Loads sessions, dropping partial/corrupt rows (id, agentId and title all
    /// required) and clearing a stale active pointer.
    /// </summary>
    public async Task<SessionsState> LoadAsync()
    {
        await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
        var state = new SessionsState
        {
            ActiveSessionId = await AppDatabase.GetMetaAsync(connection, ActiveSessionMetaKey).ConfigureAwait(false),
        };
        var pinnedIds = await LoadPinnedIdsAsync(connection).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, agent_id, title, remote_session_id, last_processed_event_id, created_at, updated_at
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
                    RemoteSessionId = ReadNullableString(reader, 3),
                    LastProcessedEventId = ReadNullableString(reader, 4),
                    CreatedAt = reader.GetString(5),
                    UpdatedAt = reader.GetString(6),
                    IsPinned = pinnedIds.Contains(reader.GetString(0)),
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

    /// <summary>
    /// Updates the mutable fields of one existing session row. <see cref="SaveAsync"/> reconciles
    /// the entire index — it upserts every session and deletes the ones that vanished — which is
    /// what you want after adding or removing a conversation, but is far too much for renaming the
    /// active one or stamping its last-seen event id, both of which happen on every message sent.
    /// Does nothing if the row isn't there; the caller is editing a session it just loaded.
    /// </summary>
    public async Task UpdateSessionAsync(SessionSummary session)
    {
        if (string.IsNullOrWhiteSpace(session.Id) ||
            string.IsNullOrWhiteSpace(session.AgentId) ||
            string.IsNullOrWhiteSpace(session.Title))
        {
            return;
        }

        await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE sessions
            SET title = $title,
                remote_session_id = $remote_session_id,
                last_processed_event_id = $last_processed_event_id,
                updated_at = $updated_at
            WHERE id = $id;
            """;
        AppDatabase.Add(command, "$title", session.Title);
        AppDatabase.Add(command, "$remote_session_id", session.RemoteSessionId);
        AppDatabase.Add(command, "$last_processed_event_id", session.LastProcessedEventId);
        AppDatabase.Add(command, "$updated_at", session.UpdatedAt);
        AppDatabase.Add(command, "$id", session.Id);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        SessionsChanged?.Invoke();
    }

    public async Task SaveAsync(SessionsState state)
    {
        await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

        // Collect valid ids so we can delete stale rows after the upsert loop.
        var keepIds = new List<string>();

        for (var index = 0; index < state.Sessions.Count; index++)
        {
            var session = state.Sessions[index];
            if (string.IsNullOrWhiteSpace(session.Id) ||
                string.IsNullOrWhiteSpace(session.AgentId) ||
                string.IsNullOrWhiteSpace(session.Title))
            {
                continue;
            }

            keepIds.Add(session.Id);

            await using var upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO sessions (id, agent_id, title, remote_session_id, last_processed_event_id, created_at, updated_at, sort_order)
                VALUES ($id, $agent_id, $title, $remote_session_id, $last_processed_event_id, $created_at, $updated_at, $sort_order)
                ON CONFLICT(id) DO UPDATE SET
                    agent_id = excluded.agent_id,
                    title = excluded.title,
                    remote_session_id = excluded.remote_session_id,
                    last_processed_event_id = excluded.last_processed_event_id,
                    updated_at = excluded.updated_at,
                    sort_order = excluded.sort_order;
                """;
            AppDatabase.Add(upsert, "$id", session.Id);
            AppDatabase.Add(upsert, "$agent_id", session.AgentId);
            AppDatabase.Add(upsert, "$title", session.Title);
            AppDatabase.Add(upsert, "$remote_session_id", session.RemoteSessionId);
            AppDatabase.Add(upsert, "$last_processed_event_id", session.LastProcessedEventId);
            AppDatabase.Add(upsert, "$created_at", session.CreatedAt);
            AppDatabase.Add(upsert, "$updated_at", session.UpdatedAt);
            AppDatabase.Add(upsert, "$sort_order", index);
            await upsert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // Remove sessions that are no longer in the list.
        // By this point, the caller has already cleaned up child rows
        // (messages/executions/traces) for removed sessions, so FK won't trip.
        if (keepIds.Count > 0)
        {
            await using var del = connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = "DELETE FROM sessions WHERE id NOT IN (";
            for (var i = 0; i < keepIds.Count; i++)
            {
                del.CommandText += i > 0 ? ", $" : "$";
                del.CommandText += $"p{i}";
                AppDatabase.Add(del, $"$p{i}", keepIds[i]);
            }
            del.CommandText += ");";
            await del.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        else
        {
            await using var del = connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = "DELETE FROM sessions;";
            await del.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(state.ActiveSessionId))
        {
            await AppDatabase.DeleteMetaAsync(connection, transaction, ActiveSessionMetaKey)
                .ConfigureAwait(false);
        }
        else
        {
            await AppDatabase.SetMetaAsync(connection, transaction, ActiveSessionMetaKey, state.ActiveSessionId!)
                .ConfigureAwait(false);
        }

        var pinnedIds = state.Sessions
            .Where(session => session.IsPinned && keepIds.Contains(session.Id))
            .Select(session => session.Id)
            .Distinct()
            .ToList();
        if (pinnedIds.Count == 0)
        {
            await AppDatabase.DeleteMetaAsync(connection, transaction, PinnedSessionsMetaKey)
                .ConfigureAwait(false);
        }
        else
        {
            await AppDatabase.SetMetaAsync(
                    connection,
                    transaction,
                    PinnedSessionsMetaKey,
                    JsonSerializer.Serialize(pinnedIds, AppJsonContext.Default.ListString))
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        SessionsChanged?.Invoke();
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static async Task<HashSet<string>> LoadPinnedIdsAsync(SqliteConnection connection)
    {
        var json = await AppDatabase.GetMetaAsync(connection, PinnedSessionsMetaKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return new HashSet<string>();

        try
        {
            var ids = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListString) ?? new();
            return ids.ToHashSet();
        }
        catch
        {
            return new HashSet<string>();
        }
    }
}
