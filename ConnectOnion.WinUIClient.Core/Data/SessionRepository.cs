using System.Text.Json;
using System.Text.Json.Serialization;
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

    // Pinned state is a JSON id list in app_meta rather than a column on `sessions`. It is a
    // small, whole-list property that is always read and written together, so a column plus a
    // migration would buy nothing over one metadata key.
    private const string PinnedSessionsMetaKey = "pinned_session_ids";

    /// <summary>Raised after <see cref="SaveAsync"/> commits, so the sidebar and any other
    /// open view refresh from a single signal instead of polling. Deliberately fires only for
    /// the whole-index save — <see cref="UpdateSessionAsync"/> edits a row the caller already
    /// has, so nothing else needs telling.</summary>
    public event System.Action? SessionsChanged;

    /// <summary>
    /// Loads sessions, dropping partial/corrupt rows (id, agentId and title all
    /// required) and clearing a stale active pointer.
    /// </summary>
    public async Task<SessionsState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await AppDatabase.OpenAsync(cancellationToken).ConfigureAwait(false);
        var state = new SessionsState
        {
            ActiveSessionId = await AppDatabase.GetMetaAsync(connection, ActiveSessionMetaKey).ConfigureAwait(false),
        };
        var pinnedIds = await LoadPinnedIdsAsync(connection).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            // Same stability rule as the agent list: sort_order is the user's arrangement,
            // then most-recent-first, then id purely so rows never swap places between reads.
            command.CommandText = """
                SELECT id, agent_id, title, remote_session_id, last_processed_event_id, created_at, updated_at, mode
                FROM sessions
                ORDER BY sort_order, updated_at DESC, id;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
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
                    // The setter rejects anything the host wouldn't honour, so a stale or unknown
                    // mode reads back as Safe rather than being silently trusted.
                    Mode = reader.GetString(7),
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
                updated_at = $updated_at,
                mode = $mode
            WHERE id = $id;
            """;
        AppDatabase.Add(command, "$title", session.Title);
        AppDatabase.Add(command, "$remote_session_id", session.RemoteSessionId);
        AppDatabase.Add(command, "$last_processed_event_id", session.LastProcessedEventId);
        AppDatabase.Add(command, "$updated_at", session.UpdatedAt);
        AppDatabase.Add(command, "$mode", session.Mode);
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
                INSERT INTO sessions (id, agent_id, title, remote_session_id, last_processed_event_id, created_at, updated_at, sort_order, mode)
                VALUES ($id, $agent_id, $title, $remote_session_id, $last_processed_event_id, $created_at, $updated_at, $sort_order, $mode)
                ON CONFLICT(id) DO UPDATE SET
                    agent_id = excluded.agent_id,
                    title = excluded.title,
                    remote_session_id = excluded.remote_session_id,
                    last_processed_event_id = excluded.last_processed_event_id,
                    updated_at = excluded.updated_at,
                    sort_order = excluded.sort_order,
                    mode = excluded.mode;
                """;
            AppDatabase.Add(upsert, "$id", session.Id);
            AppDatabase.Add(upsert, "$agent_id", session.AgentId);
            AppDatabase.Add(upsert, "$title", session.Title);
            AppDatabase.Add(upsert, "$remote_session_id", session.RemoteSessionId);
            AppDatabase.Add(upsert, "$last_processed_event_id", session.LastProcessedEventId);
            AppDatabase.Add(upsert, "$created_at", session.CreatedAt);
            AppDatabase.Add(upsert, "$updated_at", session.UpdatedAt);
            AppDatabase.Add(upsert, "$sort_order", index);
            AppDatabase.Add(upsert, "$mode", session.Mode);
            await upsert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // Remove sessions that are no longer in the list.
        // By this point, the caller has already cleaned up child rows
        // (messages/executions/traces) for removed sessions, so FK won't trip.
        //
        // That ordering is a contract this method cannot enforce, and getting it wrong is the
        // failure mode described on ConversationRepository.DeleteMessagesAsync: the FKs have
        // no ON DELETE, so a session row deleted while its messages still exist raises a
        // constraint violation and fails the whole save. Delete a conversation via
        // ConversationRepository first, then save the index.
        if (keepIds.Count > 0)
        {
            await using var del = connection.CreateCommand();
            del.Transaction = transaction;
            // The id list is built as numbered *parameters*, never interpolated text — session
            // ids are GUIDs today, but a NOT IN list assembled by string concatenation is an
            // injection waiting for the day an id becomes user-derived.
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
            // Empty keep-list means the caller really did remove every session. Special-cased
            // because "NOT IN ()" is not valid SQL — the loop above would emit it.
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

        // Intersected with keepIds so the pinned list can never outlive the sessions it names
        // — nothing enforces referential integrity on a JSON blob in app_meta, so it is
        // pruned here instead.
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
            // Unreadable pin list → nothing pinned. Losing pins is a cosmetic regression the
            // user can redo in a click; throwing here would make the whole session index
            // unloadable, which is not.
            return new HashSet<string>();
        }
    }
}
