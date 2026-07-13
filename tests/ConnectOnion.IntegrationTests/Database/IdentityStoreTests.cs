using System.Reflection;
using ConnectOnion.WinUIClient.Data;

namespace ConnectOnion.IntegrationTests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class IdentityStoreTests
{
    [Fact]
    public async Task EnsureIdentity_FirstRun_EncryptsSeedAtRestAndRoundTrips()
    {
        await DeleteStoredIdentityAsync();
        ResetStaticState();

        var generated = IdentityStore.EnsureIdentity();
        var storedSeed = await ReadStoredSeedAsync();

        Assert.NotNull(storedSeed);
        Assert.NotEqual(Convert.ToBase64String(generated.PrivateSeed.ToArray()), storedSeed);
        Assert.False(IdentityStore.WasReset);

        ResetStaticState();
        var reloaded = IdentityStore.EnsureIdentity();
        Assert.Equal(generated.Address, reloaded.Address);
        Assert.Equal(generated.PrivateSeed.ToArray(), reloaded.PrivateSeed.ToArray());
    }

    [Fact]
    public async Task EnsureIdentity_CorruptStoredSeed_ReportsResetAndPersistsReplacement()
    {
        await WriteStoredIdentityAsync("old-address", "not-valid-base64");
        ResetStaticState();
        string? reportedReason = null;
        void OnReset(string reason) => reportedReason = reason;
        IdentityStore.IdentityReset += OnReset;

        try
        {
            var replacement = IdentityStore.EnsureIdentity();

            Assert.True(IdentityStore.WasReset);
            Assert.NotNull(IdentityStore.ResetReason);
            Assert.Contains("could not be decrypted", IdentityStore.ResetReason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(IdentityStore.ResetReason, reportedReason);
            Assert.NotEqual("old-address", replacement.Address);
            Assert.NotEqual("not-valid-base64", await ReadStoredSeedAsync());
        }
        finally
        {
            IdentityStore.IdentityReset -= OnReset;
        }
    }

    private static void ResetStaticState()
    {
        var type = typeof(IdentityStore);
        type.GetField("_cached", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
        type.GetField("<WasReset>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
        type.GetField("<ResetReason>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
    }

    private static async Task DeleteStoredIdentityAsync()
    {
        await using var connection = await AppDatabase.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM identity_keys;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WriteStoredIdentityAsync(string address, string seed)
    {
        await using var connection = await AppDatabase.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO identity_keys (id, address, private_seed)
            VALUES (1, $address, $seed)
            ON CONFLICT(id) DO UPDATE SET address = excluded.address, private_seed = excluded.private_seed;
            """;
        command.Parameters.AddWithValue("$address", address);
        command.Parameters.AddWithValue("$seed", seed);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadStoredSeedAsync()
    {
        await using var connection = await AppDatabase.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT private_seed FROM identity_keys WHERE id = 1;";
        return await command.ExecuteScalarAsync() as string;
    }
}
