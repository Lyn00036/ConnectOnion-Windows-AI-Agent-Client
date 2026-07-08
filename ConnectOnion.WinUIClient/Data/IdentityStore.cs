using System;
using System.Security.Cryptography;
using System.Text;
using ConnectOnion.Protocol;

namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Loads or generates the app's Ed25519 identity and persists it to
/// <c>%AppData%\ConnectOnion\connectonion.db</c>. Native equivalent of the SDK's
/// <c>ensureKeys()</c> (browser path stored keys in localStorage). The 32-byte
/// seed is encrypted at rest with Windows DPAPI (<see cref="ProtectedData"/>,
/// <see cref="DataProtectionScope.CurrentUser"/>) and the resulting blob is stored
/// as base64 — SQLite alone is not an appropriate place for raw key material,
/// and DPAPI ties decryption to the logged-in Windows user/machine so the
/// on-disk .db file is not portable or readable outside that account.
/// </summary>
public static class IdentityStore
{
    // Extra DPAPI entropy so another process running as the same Windows user
    // cannot blindly call CryptUnprotectData on our blob without knowing this
    // value. Not a secret by itself (it's compiled into the binary) — it's
    // defense in depth on top of the per-user DPAPI master key, not instead of it.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ConnectOnion.IdentityStore.v1");

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
                SELECT private_seed
                FROM identity_keys
                WHERE id = 1;
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;

            var storedSeed = reader.GetString(0);
            if (string.IsNullOrEmpty(storedSeed)) return null;

            var protectedBytes = Convert.FromBase64String(storedSeed);
            var seed = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            if (seed.Length != 32) return null;

            return AgentIdentity.FromSeed(seed);
        }
        catch
        {
            return null;
        }
    }

    private static AgentIdentity Create()
    {
        var identity = AgentIdentity.Generate();
        Persist(identity);
        return identity;
    }

    private static void Persist(AgentIdentity identity)
    {
        try
        {
            var protectedSeed = ProtectedData.Protect(
                identity.PrivateSeed.ToArray(), Entropy, DataProtectionScope.CurrentUser);
            var storedSeed = Convert.ToBase64String(protectedSeed);

            using var connection = AppDatabase.OpenAsync().GetAwaiter().GetResult();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO identity_keys (id, address, private_seed)
                VALUES (1, $address, $private_seed)
                ON CONFLICT(id) DO UPDATE SET
                    address = excluded.address,
                    private_seed = excluded.private_seed;
                """;
            AppDatabase.Add(command, "$address", identity.Address);
            AppDatabase.Add(command, "$private_seed", storedSeed);
            command.ExecuteNonQuery();
        }
        catch
        {
            // Persist is best-effort; a fresh identity per run still works.
        }
    }
}
