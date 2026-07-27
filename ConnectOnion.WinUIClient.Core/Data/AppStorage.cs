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
    public const string DataRootEnvironmentVariable =
        "CONNECTONION_DATA_ROOT";

    private static readonly StorageLocation Location = ResolveStorageLocation();

    public static readonly string RootDir = Location.RootDir;

    /// <summary>
    /// Legacy or auxiliary conversation file storage.
    /// Conversation records themselves are primarily stored in SQLite.
    /// </summary>
    public static readonly string ConversationsDir =
        Path.Combine(RootDir, "conversations");

    /// <summary>
    /// Content store for user-selected and agent-produced conversation images.
    ///
    /// Files are named by content hash so storing the same image is idempotent,
    /// and remote filenames are never trusted as local paths.
    /// </summary>
    public static readonly string ImageCacheDir =
        Path.Combine(RootDir, "cache", "images");

    /// <summary>
    /// Permanent storage for user-selected Agent icons.
    ///
    /// The database stores only a relative path such as:
    /// <code>
    /// avatars/agent-123.png
    /// </code>
    ///
    /// The image itself is stored in this directory.
    /// </summary>
    public static readonly string AgentIconsDir =
        Path.Combine(RootDir, "avatars");

    /// <summary>
    /// Temporary storage used while an Agent icon is being selected.
    ///
    /// For example, Add Agent may place a processed image here while the form
    /// is open. If the user confirms the form, the image is moved to
    /// <see cref="AgentIconsDir"/>. If the user cancels, the temporary image
    /// can be deleted without leaving an unused permanent file.
    /// </summary>
    public static readonly string TemporaryAgentIconsDir =
        Path.Combine(RootDir, "temp", "avatars");

    /// <summary>
    /// Rolling structured application logs.
    /// </summary>
    public static readonly string LogsDir =
        Path.Combine(RootDir, "logs");

    public static void EnsureDirectories()
    {
        MigrateLegacyPackagedData();
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(ConversationsDir);
        Directory.CreateDirectory(ImageCacheDir);
        Directory.CreateDirectory(AgentIconsDir);
        Directory.CreateDirectory(TemporaryAgentIconsDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>
    /// Returns an absolute path under the application's root directory.
    /// </summary>
    public static string PathFor(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return Path.Combine(RootDir, fileName);
    }

    /// <summary>
    /// Creates the relative path that should be stored in
    /// <c>agents.icon_path</c>.
    ///
    /// For example:
    /// <code>
    /// CreateAgentIconRelativePath("agent-123.png")
    /// // returns "avatars/agent-123.png"
    /// </code>
    ///
    /// Forward slashes are used in the persisted value so the stored path
    /// remains stable and independent of Windows path formatting.
    /// </summary>
    public static string CreateAgentIconRelativePath(string fileName)
    {
        var safeFileName = ValidateLeafFileName(fileName);

        return $"avatars/{safeFileName}";
    }

    /// <summary>
    /// Resolves an Agent icon path stored in SQLite into an absolute local
    /// filesystem path.
    ///
    /// The supplied path must resolve inside <see cref="AgentIconsDir"/>.
    /// Paths that attempt to escape the avatar directory are rejected.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The relative path is null, empty, or contains only whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The path resolves outside the managed Agent icon directory.
    /// </exception>
    public static string GetAgentIconAbsolutePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var absolutePath = Path.GetFullPath(
            Path.Combine(RootDir, normalizedRelativePath));

        EnsurePathInsideDirectory(
            absolutePath,
            AgentIconsDir,
            "The Agent icon path must remain inside the managed avatars directory.");

        return absolutePath;
    }

    /// <summary>
    /// Attempts to resolve a stored Agent icon path.
    ///
    /// Returns <see langword="false"/> for an empty, malformed, or unsafe path.
    /// This is suitable for UI fallback behavior where an invalid path should
    /// display the default Agent avatar instead of crashing the application.
    /// </summary>
    public static bool TryGetAgentIconAbsolutePath(
        string? relativePath,
        out string? absolutePath)
    {
        absolutePath = null;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        try
        {
            absolutePath = GetAgentIconAbsolutePath(relativePath);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns an absolute path in the temporary Agent icon directory.
    ///
    /// Only a plain filename is accepted. Directory components such as
    /// <c>../</c> or <c>folder/image.png</c> are rejected.
    /// </summary>
    public static string GetTemporaryAgentIconAbsolutePath(string fileName)
    {
        var safeFileName = ValidateLeafFileName(fileName);

        return Path.Combine(TemporaryAgentIconsDir, safeFileName);
    }

    /// <summary>
    /// Returns an absolute path in the permanent Agent icon directory.
    ///
    /// This method is useful when committing a processed temporary icon into
    /// permanent storage.
    /// </summary>
    public static string GetPermanentAgentIconAbsolutePath(string fileName)
    {
        var safeFileName = ValidateLeafFileName(fileName);

        return Path.Combine(AgentIconsDir, safeFileName);
    }

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
