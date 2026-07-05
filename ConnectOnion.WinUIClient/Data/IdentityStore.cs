using System;
using System.Text.Json.Serialization;
using ConnectOnion.Protocol;

namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Loads or generates the app's Ed25519 identity and persists it to
/// <c>%AppData%\ConnectOnion\connectonion.db</c>. Native equivalent of the SDK's
/// <c>ensureKeys()</c> (browser path stored keys in localStorage). The 32-byte
/// seed is stored as hex; the public address is derived on load.
/// </summary>
public static class IdentityStore
{
    private sealed class StoredKeys
    {
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("privateSeed")] public string PrivateSeed { get; set; } = "";
    }

    private static AgentIdentity? _cached;

    public static AgentIdentity EnsureIdentity()
    {
        if (_cached is not null) return _cached;

        _cached = Load() ?? Create();
        return _cached;
    }

    private static AgentIdentity? Load()
    {
        try
        {
            using var connection = AppDatabase.OpenAsync().GetAwaiter().GetResult();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT address, private_seed
                FROM identity_keys
                WHERE id = 1;
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            var stored = new StoredKeys
            {
                Address = reader.GetString(0),
                PrivateSeed = reader.GetString(1),
            };
            if (stored is null || string.IsNullOrEmpty(stored.PrivateSeed)) return null;
            return AgentIdentity.FromSeed(Convert.FromHexString(stored.PrivateSeed));
        }
        catch
        {
            return null;
        }
    }

    private static AgentIdentity Create()
    {
        var identity = AgentIdentity.Generate();
        try
        {
            var stored = new StoredKeys
            {
                Address = identity.Address,
                PrivateSeed = Hex.ToLowerString(identity.PrivateSeed),
            };

            using var connection = AppDatabase.OpenAsync().GetAwaiter().GetResult();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO identity_keys (id, address, private_seed)
                VALUES (1, $address, $private_seed)
                ON CONFLICT(id) DO UPDATE SET
                    address = excluded.address,
                    private_seed = excluded.private_seed;
                """;
            AppDatabase.Add(command, "$address", stored.Address);
            AppDatabase.Add(command, "$private_seed", stored.PrivateSeed);
            command.ExecuteNonQuery();
        }
        catch
        {
            // Persist is best-effort; a fresh identity per run still works.
        }
        return identity;
    }
}
