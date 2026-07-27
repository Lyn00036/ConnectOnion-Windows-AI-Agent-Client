namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Shared filesystem plumbing for the local repositories.
///
/// Unpackaged data lives under <c>%AppData%\ConnectOnion</c>; an installed MSIX normally uses its
/// package <c>LocalState\ConnectOnion</c> folder. Existing installations that already have a
/// SQLite database under <c>%AppData%\ConnectOnion</c> continue to use that database so saved
/// Agents, sessions, preferences, and conversations remain visible.
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

    private const string DatabaseFileName = "connectonion.db";

    private static readonly StorageLocation Location = ResolveStorageLocation();

    public static readonly string RootDir = Location.RootDir;

    public static readonly string ConversationsDir = Path.Combine(RootDir, "conversations");

    /// <summary>
    /// Content store for user-selected and agent-produced conversation images.
    /// Files are named by content hash so storing the same image is idempotent
    /// and remote filenames are never trusted as local paths.
    /// </summary>
    public static readonly string ImageCacheDir = Path.Combine(RootDir, "cache", "images");

    /// <summary>
    /// Permanent storage for user-selected Agent icons.
    /// The database stores a relative path such as <c>avatars/agent-123.png</c>.
    /// </summary>
    public static readonly string AgentIconsDir = Path.Combine(RootDir, "avatars");

    /// <summary>
    /// Temporary storage used while an Agent icon is selected and processed.
    /// </summary>
    public static readonly string TemporaryAgentIconsDir =
        Path.Combine(RootDir, "temp", "avatars");

    /// <summary>Rolling structured application logs.</summary>
    public static readonly string LogsDir = Path.Combine(RootDir, "logs");

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

    public static string PathFor(string fileName) => Path.Combine(RootDir, fileName);

    /// <summary>
    /// Creates the relative path persisted for an Agent icon.
    /// </summary>
    public static string CreateAgentIconRelativePath(string fileName)
    {
        var safeFileName = ValidateLeafFileName(fileName);
        return $"avatars/{safeFileName}";
    }

    /// <summary>
    /// Resolves a persisted Agent icon path to its absolute managed path.
    /// </summary>
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
    /// Tries to resolve a persisted Agent icon path for UI fallback scenarios.
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

    public static string GetTemporaryAgentIconAbsolutePath(string fileName)
    {
        var safeFileName = ValidateLeafFileName(fileName);
        return Path.Combine(TemporaryAgentIconsDir, safeFileName);
    }

    public static string GetPermanentAgentIconAbsolutePath(string fileName)
    {
        var safeFileName = ValidateLeafFileName(fileName);
        return Path.Combine(AgentIconsDir, safeFileName);
    }

    private static string ValidateLeafFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var trimmed = fileName.Trim();
        var leafFileName = Path.GetFileName(trimmed);

        if (!string.Equals(trimmed, leafFileName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The value must contain a filename without directory components.",
                nameof(fileName));
        }

        if (leafFileName is "." or "..")
        {
            throw new ArgumentException(
                "The value is not a valid filename.",
                nameof(fileName));
        }

        if (leafFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "The filename contains invalid characters.",
                nameof(fileName));
        }

        return leafFileName;
    }

    private static void EnsurePathInsideDirectory(
        string candidatePath,
        string expectedDirectory,
        string errorMessage)
    {
        var candidateFullPath = Path.GetFullPath(candidatePath);

        var directoryFullPath = Path
            .GetFullPath(expectedDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!candidateFullPath.StartsWith(
                directoryFullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(errorMessage);
        }
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

        /*
         * Use APPDATA first instead of relying only on SpecialFolder.ApplicationData.
         * In a packaged process SpecialFolder.ApplicationData can be redirected to the
         * package's LocalCache\Roaming directory, while APPDATA still identifies the
         * existing desktop-client store that contains the user's saved SQLite database.
         */
        var roamingAppDataFolder = ResolveRoamingAppDataFolder();
        var roamingRoot = SelectRootDir(
            configured: null,
            packagedLocalFolder: null,
            roamingAppDataFolder: roamingAppDataFolder);

        try
        {
            // ApplicationData requires package identity.
            var applicationData = Windows.Storage.ApplicationData.Current;
            var packagedRoot = SelectRootDir(
                configured: null,
                packagedLocalFolder: applicationData.LocalFolder.Path,
                roamingAppDataFolder: null);

            /*
             * Compatibility rule:
             * If the existing desktop store already contains connectonion.db,
             * keep using it. This prevents a packaged/debug launch from opening
             * a new empty database and making the saved Agents appear missing.
             *
             * Fresh installations with no existing roaming database continue
             * to use the main branch's LocalState location.
             */
            if (HasExistingDatabase(roamingRoot))
            {
                return new StorageLocation(roamingRoot, null);
            }

            // Preserve main's migration path for older packaged development builds.
            var legacyRoot = Path.Combine(
                applicationData.LocalCacheFolder.Path,
                "Roaming",
                "ConnectOnion");

            return new StorageLocation(packagedRoot, legacyRoot);
        }
        catch (InvalidOperationException)
        {
            // Unpackaged applications have no ApplicationData.Current.
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Some unpackaged Windows App SDK hosts report the missing identity as a COM error.
        }

        return new StorageLocation(roamingRoot, null);
    }

    private static string ResolveRoamingAppDataFolder()
    {
        var environmentAppData = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrWhiteSpace(environmentAppData))
        {
            return Path.GetFullPath(environmentAppData);
        }

        var specialFolderAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        ArgumentException.ThrowIfNullOrWhiteSpace(specialFolderAppData);
        return Path.GetFullPath(specialFolderAppData);
    }

    private static bool HasExistingDatabase(string rootDirectory)
    {
        return File.Exists(Path.Combine(rootDirectory, DatabaseFileName));
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
