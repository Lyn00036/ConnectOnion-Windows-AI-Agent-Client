namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Shared filesystem plumbing for the local repositories.
///
/// Unpackaged data lives under <c>%AppData%\ConnectOnion</c>; an installed MSIX uses its
/// package <c>LocalState\ConnectOnion</c> folder. SQLite is the active store:
/// <code>
///   connectonion.db
/// </code>
///
/// <para><b>Deliberately holds no JSON helper.</b> It used to carry a generic
/// <c>ReadAsync&lt;T&gt;</c>/<c>WriteAsync&lt;T&gt;</c> file round-trip left over from the
/// pre-SQLite store, with no caller anywhere in the app. Those overloads are reflection-based,
/// so they contributed <c>IL2026</c> to the trim inventory and would have thrown
/// <see cref="System.NotSupportedException"/> the first time anyone did call them from a trimmed
/// build. A JSON blob that belongs to us goes through a source-generated context
/// (<see cref="AppJsonContext"/>, <see cref="ConversationJsonContext"/>); anything shaped by the
/// host goes through <see cref="ConnectOnion.Protocol.WireJson"/>. There is no third way, and
/// that is the point — see <c>docs/TRIMMING.md</c>.</para>
/// </summary>
public static class AppStorage
{
    /// <summary>
    /// Optional data-root override for isolated automation and portable development runs.
    /// Production leaves this unset and selects the root from the deployment model.
    /// </summary>
    public const string DataRootEnvironmentVariable = "CONNECTONION_DATA_ROOT";

    private static readonly StorageLocation Location = ResolveStorageLocation();

    public static readonly string RootDir = Location.RootDir;

    public static readonly string ConversationsDir = Path.Combine(RootDir, "conversations");

    /// <summary>
    /// Content store for user-selected and agent-produced conversation images.
    /// Files are named by content hash so storing the same image is idempotent
    /// and remote filenames are never trusted as local paths.
    /// </summary>
    public static readonly string ImageCacheDir = Path.Combine(RootDir, "cache", "images");

    /// <summary>Rolling structured application logs.</summary>
    public static readonly string LogsDir = Path.Combine(RootDir, "logs");

    public static void EnsureDirectories()
    {
        MigrateLegacyPackagedData();
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(ConversationsDir);
        Directory.CreateDirectory(ImageCacheDir);
        Directory.CreateDirectory(LogsDir);
    }

    public static string PathFor(string fileName) => Path.Combine(RootDir, fileName);

    private static StorageLocation ResolveStorageLocation()
    {
        var configured = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new StorageLocation(
                SelectRootDir(configured, packagedLocalFolder: null, roamingAppDataFolder: null),
                null);
        }

        try
        {
            // ApplicationData requires package identity. Keeping it behind this guarded branch
            // preserves the unpackaged CLI/portable path while making an installed MSIX use the
            // stable LocalState location that Windows backs up and retains across upgrades.
            var applicationData = Windows.Storage.ApplicationData.Current;
            var root = SelectRootDir(
                configured: null,
                packagedLocalFolder: applicationData.LocalFolder.Path,
                roamingAppDataFolder: null);

            // Older development builds wrote through Environment.SpecialFolder.ApplicationData.
            // Windows virtualized those writes into LocalCache\Roaming. Migrate that one legacy
            // location before the first LocalState directory is created so existing developer
            // conversations and the DPAPI-protected identity are not silently abandoned.
            var legacyRoot = Path.Combine(
                applicationData.LocalCacheFolder.Path,
                "Roaming",
                "ConnectOnion");
            return new StorageLocation(root, legacyRoot);
        }
        catch (InvalidOperationException)
        {
            // Unpackaged applications have no ApplicationData.Current.
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Some unpackaged Windows App SDK hosts report the missing identity as a COM error.
        }

        return new StorageLocation(
            SelectRootDir(
                configured: null,
                packagedLocalFolder: null,
                roamingAppDataFolder: Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData)),
            null);
    }

    internal static string SelectRootDir(
        string? configured,
        string? packagedLocalFolder,
        string? roamingAppDataFolder)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        if (!string.IsNullOrWhiteSpace(packagedLocalFolder))
        {
            return Path.Combine(packagedLocalFolder, "ConnectOnion");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(roamingAppDataFolder);
        return Path.Combine(roamingAppDataFolder, "ConnectOnion");
    }

    private static void MigrateLegacyPackagedData()
    {
        var legacyRoot = Location.LegacyRootDir;
        if (legacyRoot is null ||
            Directory.Exists(RootDir) ||
            !Directory.Exists(legacyRoot))
        {
            return;
        }

        var parent = Path.GetDirectoryName(RootDir);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        Directory.Move(legacyRoot, RootDir);
    }

    private sealed record StorageLocation(string RootDir, string? LegacyRootDir);
}
