using System;
using System.Threading.Tasks;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Per-conversation blob persistence. Port of <c>conversationStorage.ts</c>.
///
/// The web app keyed conversations by <c>co:agent:{address}:session:{id}</c> in
/// localStorage. SQLite stores that logical key directly as the primary key so
/// diagnostics stay readable.
/// </summary>
public sealed class ConversationRepository
{
    /// <summary>Logical key, kept identical to the web app for parity/diagnostics.</summary>
    public static string StorageKey(string address, string sessionId)
        => $"co:agent:{address}:session:{sessionId}";

    /// <summary>Reads the raw conversation envelope JSON, or null if none exists.</summary>
    public async Task<string?> ReadEnvelopeAsync(string address, string sessionId)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT envelope_json
                FROM conversations
                WHERE storage_key = $storage_key;
                """;
            AppDatabase.Add(command, "$storage_key", StorageKey(address, sessionId));
            return await command.ExecuteScalarAsync().ConfigureAwait(false) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Writes the raw conversation envelope JSON atomically.</summary>
    public async Task WriteEnvelopeAsync(string address, string sessionId, string envelopeJson)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO conversations (storage_key, address, session_id, envelope_json, updated_at)
                VALUES ($storage_key, $address, $session_id, $envelope_json, $updated_at)
                ON CONFLICT(storage_key) DO UPDATE SET
                    address = excluded.address,
                    session_id = excluded.session_id,
                    envelope_json = excluded.envelope_json,
                    updated_at = excluded.updated_at;
                """;
            AppDatabase.Add(command, "$storage_key", StorageKey(address, sessionId));
            AppDatabase.Add(command, "$address", address);
            AppDatabase.Add(command, "$session_id", sessionId);
            AppDatabase.Add(command, "$envelope_json", envelopeJson);
            AppDatabase.Add(command, "$updated_at", DateTime.UtcNow.ToString("o"));
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: keep the live conversation working if the write fails.
        }
    }

    public void RemoveEnvelope(string address, string sessionId)
    {
        try
        {
            using var connection = AppDatabase.OpenAsync().GetAwaiter().GetResult();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM conversations WHERE storage_key = $storage_key;";
            AppDatabase.Add(command, "$storage_key", StorageKey(address, sessionId));
            command.ExecuteNonQuery();
        }
        catch
        {
            // Ignore delete failures; a stale blob is harmless.
        }
    }
}
