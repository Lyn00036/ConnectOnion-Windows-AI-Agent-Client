using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// Stable palette avatar shared across the home cards, Agent detail page,
/// chat header, and sidebar.
///
/// When <see cref="ImagePath"/> points to a valid app-managed image, the
/// custom image is displayed. Otherwise, the control falls back to the
/// Agent initial and deterministic palette.
///
/// The visual size is controlled by <see cref="Size"/>. Corner radius and
/// font size reuse the inherited
/// <see cref="Control.CornerRadius"/> and <see cref="Control.FontSize"/>
/// properties.
/// </summary>
public sealed partial class AgentAvatar : UserControl
{
    private const string FallbackInitialState =
        "FallbackInitial";

    private const string CustomImageState =
        "CustomImage";

    private static readonly string[] PaletteStateNames =
    {
        "Blue",
        "Violet",
        "Teal",
        "Coral",
        "Indigo",
        "Cyan",
        "Emerald",
        "Amber",
        "Rose",
        "Plum",
        "Slate",
        "Olive",
    };

    public static readonly DependencyProperty InitialProperty =
        DependencyProperty.Register(
            nameof(Initial),
            typeof(string),
            typeof(AgentAvatar),
            new PropertyMetadata(
                "?",
                OnInitialChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(
            nameof(Size),
            typeof(double),
            typeof(AgentAvatar),
            new PropertyMetadata(32.0));

    public static readonly DependencyProperty PaletteIndexProperty =
        DependencyProperty.Register(
            nameof(PaletteIndex),
            typeof(int),
            typeof(AgentAvatar),
            new PropertyMetadata(
                -1,
                OnPaletteIndexChanged));

    /// <summary>
    /// Dependency property for <see cref="ImagePath"/>.
    /// </summary>
    public static readonly DependencyProperty ImagePathProperty =
        DependencyProperty.Register(
            nameof(ImagePath),
            typeof(string),
            typeof(AgentAvatar),
            new PropertyMetadata(
                null,
                OnImagePathChanged));

    /*
     * Incremented whenever ImagePath changes.
     *
     * If an older asynchronous image load completes after a newer one,
     * its version no longer matches and it is ignored. This prevents an
     * old Agent image from appearing after the control has been rebound.
     */
    private int _imageLoadVersion;

    public AgentAvatar()
    {
        InitializeComponent();

        ApplyPalette();
        ShowFallbackInitial();
    }

    /// <summary>
    /// Initial displayed when no valid custom image is available.
    /// </summary>
    public string Initial
    {
        get => (string)GetValue(InitialProperty);
        set => SetValue(InitialProperty, value);
    }

    /// <summary>
    /// Width and height of the avatar.
    /// </summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>
    /// Stable palette slot.
    ///
    /// Negative values derive a deterministic palette from
    /// <see cref="Initial"/>.
    /// </summary>
    public int PaletteIndex
    {
        get => (int)GetValue(PaletteIndexProperty);
        set => SetValue(PaletteIndexProperty, value);
    }

    /// <summary>
    /// Path to the custom Agent image.
    ///
    /// Committed Agent icons normally use an app-relative path such as:
    /// <code>
    /// avatars/agent-example.png
    /// </code>
    ///
    /// An absolute path is also accepted when it points inside the
    /// app-managed permanent or temporary Agent-icon directories. This
    /// supports previewing an image before Add Agent or Edit Agent is saved.
    /// </summary>
    public string? ImagePath
    {
        get => (string?)GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    /// <summary>
    /// Forces the current custom image path to be decoded again.
    ///
    /// This is required when an Agent icon file is replaced while its stored
    /// path remains unchanged. In that case, the ImagePath dependency-property
    /// callback does not run because the string value is still the same.
    /// </summary>
    public void ReloadImage()
    {
        BeginLoadCustomImage();
    }

    private static void OnPaletteIndexChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        ((AgentAvatar)sender).ApplyPalette();
    }

    private static void OnInitialChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        ((AgentAvatar)sender).ApplyPalette();
    }

    private static void OnImagePathChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        ((AgentAvatar)sender).BeginLoadCustomImage();
    }

    /// <summary>
    /// Applies the deterministic fallback palette.
    /// </summary>
    private void ApplyPalette()
    {
        string state;

        if (PaletteIndex < 0 &&
            string.IsNullOrWhiteSpace(Initial))
        {
            state = "Neutral";
        }
        else
        {
            var resolvedIndex = PaletteIndex < 0
                ? AvatarPalette.From(Initial)
                : PaletteIndex % PaletteStateNames.Length;

            // Protect against an unexpected negative result from a future
            // palette implementation.
            if (resolvedIndex < 0)
            {
                resolvedIndex += PaletteStateNames.Length;
            }

            state = PaletteStateNames[resolvedIndex];
        }

        VisualStateManager.GoToState(
            this,
            state,
            useTransitions: false);
    }

    /// <summary>
    /// Starts loading the current <see cref="ImagePath"/>.
    /// </summary>
    private void BeginLoadCustomImage()
    {
        var loadVersion =
            Interlocked.Increment(ref _imageLoadVersion);

        var configuredPath = ImagePath;

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            ShowFallbackInitial();
            return;
        }

        // The asynchronous method catches all expected loading failures.
        // It must not allow an exception to escape this fire-and-forget call.
        _ = LoadCustomImageAsync(
            configuredPath,
            loadVersion);
    }

    /// <summary>
    /// Resolves, reads, and displays a custom Agent image.
    /// </summary>
    private async Task LoadCustomImageAsync(
        string configuredPath,
        int loadVersion)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] LoadCustomImageAsync START: configuredPath='{configuredPath}'");

            var absolutePath =
                ResolveManagedImagePath(configuredPath);

            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] ResolveManagedImagePath returned: '{absolutePath}'");

            if (absolutePath is null ||
                !File.Exists(absolutePath))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AgentAvatar] Path is null or file doesn't exist, showing fallback");
                ShowFallbackIfCurrent(loadVersion);
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] Getting StorageFile from: {absolutePath}");

            var storageFile =
                await StorageFile.GetFileFromPathAsync(
                    absolutePath);

            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] StorageFile obtained, opening stream...");

            using var stream =
                await storageFile.OpenAsync(
                    FileAccessMode.Read);

            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] Stream opened, creating BitmapImage...");

            var bitmap = new BitmapImage();

            await bitmap.SetSourceAsync(stream);

            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] BitmapImage SetSourceAsync completed");

            // ImagePath may have changed while the previous file was being
            // opened and decoded. Never apply an outdated result.
            if (loadVersion !=
                Volatile.Read(ref _imageLoadVersion))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AgentAvatar] Version mismatch, ignoring result");
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] Assigning bitmap to CustomAvatarImage.Source");

            CustomAvatarImage.Source = bitmap;

            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] CustomAvatarImage.Source assigned: {(bitmap != null ? "valid bitmap" : "null")}");
            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] CustomAvatarImage.Visibility BEFORE change: {CustomAvatarImage.Visibility}");

            // CRITICAL FIX: Directly set visibility instead of relying on VisualState
            // VisualStateManager.GoToState may not work in async context
            InitialText.Visibility = Visibility.Collapsed;
            CustomAvatarImage.Visibility = Visibility.Visible;

            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] CustomAvatarImage.Visibility AFTER direct change: {CustomAvatarImage.Visibility}");
            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] LoadCustomImageAsync COMPLETED SUCCESSFULLY");
        }
        catch (Exception ex)
        {
            // Missing, corrupt, inaccessible, or unsupported local images
            // must not break the Agent list. The initial avatar is the safe
            // and deterministic fallback.
            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] Error loading image from '{configuredPath}': {ex.GetType().Name}: {ex.Message}");
            ShowFallbackIfCurrent(loadVersion);
        }
    }

    /// <summary>
    /// Resolves a relative committed icon path or an absolute temporary
    /// preview path.
    /// </summary>
    private static string? ResolveManagedImagePath(
        string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        try
        {
            var trimmedPath = configuredPath.Trim();

            if (!Path.IsPathFullyQualified(trimmedPath))
            {
                var resolved = AppStorage.GetAgentIconAbsolutePath(
                    trimmedPath);
                System.Diagnostics.Debug.WriteLine(
                    $"[AgentAvatar] Resolved relative path '{trimmedPath}' to '{resolved}'");
                return resolved;
            }

            var absolutePath =
                Path.GetFullPath(trimmedPath);

            if (IsPathInsideDirectory(
                    absolutePath,
                    AppStorage.AgentIconsDir) ||
                IsPathInsideDirectory(
                    absolutePath,
                    AppStorage.TemporaryAgentIconsDir))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AgentAvatar] Absolute path '{absolutePath}' is within managed directory");
                return absolutePath;
            }

            // Do not display arbitrary absolute paths supplied through a
            // modified database or unexpected binding source.
            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] Absolute path '{absolutePath}' is NOT within managed directory");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AgentAvatar] Exception in ResolveManagedImagePath: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns true when a file path is contained inside an expected
    /// app-managed directory.
    /// </summary>
    private static bool IsPathInsideDirectory(
        string candidatePath,
        string expectedDirectory)
    {
        var candidateFullPath =
            Path.GetFullPath(candidatePath);

        var directoryFullPath =
            Path.GetFullPath(expectedDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return candidateFullPath.StartsWith(
            directoryFullPath,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Restores the initial avatar only if this load operation still
    /// corresponds to the current ImagePath.
    /// </summary>
    private void ShowFallbackIfCurrent(
        int loadVersion)
    {
        if (loadVersion !=
            Volatile.Read(ref _imageLoadVersion))
        {
            return;
        }

        ShowFallbackInitial();
    }

    /// <summary>
    /// Clears the custom image and displays the initial avatar.
    /// </summary>
    private void ShowFallbackInitial()
    {
        CustomAvatarImage.Source = null;

        // Direct visibility change instead of VisualStateManager
        InitialText.Visibility = Visibility.Visible;
        CustomAvatarImage.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Handles failures that occur after the BitmapImage source has been
    /// assigned to the Image control.
    /// </summary>
    private void CustomImage_ImageFailed(
        object sender,
        ExceptionRoutedEventArgs args)
    {
        // Invalidate any in-progress operation before clearing the image.
        Interlocked.Increment(ref _imageLoadVersion);

        ShowFallbackInitial();
    }
}
