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

    // Process-lifetime cache. The identity cannot change while the app runs (nothing offers
    // to regenerate it), so this is a pure memoization — and it matters, because EnsureIdentity
    // is called synchronously from connection setup and would otherwise hit the database and
    // DPAPI on every connect.
    private static AgentIdentity? _cached;

    /// <summary>
    /// Raised when an identity row existed on disk but could not be recovered, so a brand-new
    /// identity was generated in its place. This is not a routine event: the app's address changes,
    /// and any authorization an agent had granted the old address is gone. The user has to be told
    /// — silently swapping identities is how someone loses access without ever knowing why.
    /// Fires once per process, before the replacement identity is returned.
    /// </summary>
    public static event Action<string>? IdentityReset;

    /// <summary>
    /// Set when <see cref="EnsureIdentity"/> had to discard an unreadable identity. Latched because
    /// the identity is usually first needed before any window exists to show a message — the shell
    /// checks this on load (see <c>MainWindow.Notifications.cs</c>) and reports it then.
    /// </summary>
    public static bool WasReset { get; private set; }

    /// <summary>Human-readable reason for <see cref="WasReset"/>, or null.</summary>
    public static string? ResetReason { get; private set; }

    public static AgentIdentity EnsureIdentity()
    {
        if (_cached is not null) return _cached;

        var (identity, fault) = Load();
        if (identity is not null)
        {
            _cached = identity;
            return _cached;
        }

        // No row at all is the normal first-run path — generate quietly. A row we could not read is
        // a different story: report it, because the old identity is being thrown away.
        if (fault is not null) ReportReset(fault);

        _cached = Create();
        return _cached;
    }

    /// <summary>
    /// Reads the stored identity. Returns <c>(null, null)</c> when there is simply nothing stored
    /// yet (first run), and <c>(null, reason)</c> when a row exists but is unusable — the caller
    /// must not treat those two the same, which is exactly the bug this split fixes.
    /// </summary>
    private static (AgentIdentity? Identity, string? Fault) Load()
    {
        string storedSeed;

        try
        {
            // Blocking is safe only because AppDatabase.OpenAsync awaits with
            // ConfigureAwait(false) the whole way down, so no continuation captures the UI
            // context EnsureIdentity is called on. Keep that true or make this async.
#pragma warning disable VSTHRD002
            using var connection = AppDatabase.OpenAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT private_seed
                FROM identity_keys
                WHERE id = 1;
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return (null, null);          // first run — nothing stored

            storedSeed = reader.GetString(0);
            if (string.IsNullOrEmpty(storedSeed))
                return (null, "the stored identity was empty");
        }
        catch (Exception ex)
        {
            // The database itself is unreadable. Don't claim the identity was lost — we don't know
            // that — but don't pretend this is a clean first run either.
            return (null, $"the identity could not be read from the database ({ex.Message})");
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(storedSeed);
            var seed = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);

            if (seed.Length != 32)
                return (null, "the stored identity was the wrong size");

            return (AgentIdentity.FromSeed(seed), null);
        }
        catch (Exception ex)
        {
            // DPAPI is scoped to the Windows user + machine. The usual causes are a copied .db file,
            // a restored user profile, or a database written before seeds were encrypted at all.
            return (null, $"the stored identity could not be decrypted ({ex.Message})");
        }
    }

    private static void ReportReset(string reason)
    {
        WasReset = true;
        ResetReason = reason;

        System.Diagnostics.Debug.WriteLine($"[IdentityStore] identity reset: {reason}");

        try { IdentityReset?.Invoke(reason); }
        catch { /* a listener must never block identity creation */ }
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

            // Safe for the same reason as Load's open — see the note there.
#pragma warning disable VSTHRD002
            using var connection = AppDatabase.OpenAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
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
            // Persist is best-effort; a fresh identity per run still works — but "works" here
            // means the app functions, not that the user is unaffected. An identity that never
            // persists gives a new address on every launch, so every agent has to re-authorize
            // this client each time. If that is ever reported, this silent catch is the place
            // to look: unlike the unreadable-identity path above, it raises no IdentityReset
            // and sets no WasReset flag, so nothing surfaces it.
        }
    }
}
