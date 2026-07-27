using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// The agent's face, shared by the home cards, the agent detail page, the chat header and the
/// sidebar. Shows the user's chosen image when <see cref="ImagePath"/> resolves to one, and the
/// name initial on its stable palette colour when it does not — missing, unreadable and never-set
/// all land on the initial, because an agent row with a blank avatar is worse than a lettered one.
/// </summary>
public sealed partial class AgentAvatar : UserControl
{
    /// <summary>
    /// Must match the <c>PaletteStates</c> visual states in the XAML, in order — the index picks a
    /// state by position. Those states live on the control's <i>content root</i>: GoToState looks
    /// the group up on the control's first child, so a group declared any deeper is silently never
    /// found and every avatar quietly falls back to the neutral colours.
    /// </summary>
    private static readonly string[] PaletteStateNames =
    [
        "Blue", "Violet", "Teal", "Coral", "Indigo", "Cyan",
        "Emerald", "Amber", "Rose", "Plum", "Slate", "Olive",
    ];

    /// <summary>Committed icons are 256px squares but the largest slot on screen is 56px, so the
    /// decode is capped: decoding at full size costs ~256 KB of pixels to draw a 28px circle, and
    /// the sidebar builds one of these per agent.</summary>
    private const int MaxDecodePixels = 96;

    private const int MaxCachedImages = 16;

    // The sidebar rebuilds its rows on every refresh, so the same handful of files are decoded
    // again and again. Weak references let the bitmaps go once no avatar is showing them, which
    // matters because each one owns a native surface (see the memory notes in CLAUDE.md).
    // All access is from the UI thread, so a plain dictionary + FIFO queue is enough.
    private static readonly Dictionary<string, WeakReference<BitmapImage>> ImageCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Queue<string> ImageCacheOrder = new();

    public static readonly DependencyProperty InitialProperty =
        DependencyProperty.Register(
            nameof(Initial),
            typeof(string),
            typeof(AgentAvatar),
            new PropertyMetadata("?", OnPaletteInputChanged));

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
            new PropertyMetadata(-1, OnPaletteInputChanged));

    public static readonly DependencyProperty ImagePathProperty =
        DependencyProperty.Register(
            nameof(ImagePath),
            typeof(string),
            typeof(AgentAvatar),
            new PropertyMetadata(null, OnImagePathChanged));

    /// <summary>
    /// Bumped on every <see cref="ImagePath"/> change so a decode that finishes after the control
    /// was rebound is discarded. Recycled rows change path faster than a disk read completes, and
    /// without this the previous agent's face lands on the current agent's row.
    /// </summary>
    private int _imageLoadVersion;

    /// <summary>
    /// True while a custom image is on screen. The palette is the initial's identity colour, so an
    /// agent showing its own picture drops it entirely — the picture already identifies the agent,
    /// and tinting the ring around it competes with whatever colours are in the image.
    /// </summary>
    private bool _showingCustomImage;

    public AgentAvatar()
    {
        InitializeComponent();
        ApplyPalette();
    }

    /// <summary>Letter shown when no custom image is available.</summary>
    public string Initial
    {
        get => (string)GetValue(InitialProperty);
        set => SetValue(InitialProperty, value);
    }

    /// <summary>Width and height of the avatar.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>
    /// Palette slot for the fallback initial. Negative derives one from <see cref="Initial"/>, so
    /// a caller that has no stable agent name to hash still gets a consistent colour.
    /// </summary>
    public int PaletteIndex
    {
        get => (int)GetValue(PaletteIndexProperty);
        set => SetValue(PaletteIndexProperty, value);
    }

    /// <summary>
    /// The image to show. Normally the stored relative path (<c>avatars/agent-….png</c>), but an
    /// absolute path is accepted too so the Add Agent form can preview a pick that has not been
    /// committed yet. Either way it must land inside the managed avatar directories —
    /// <c>icon_path</c> is a database column, and a hand-edited one must not turn this control
    /// into a viewer for arbitrary files on disk.
    /// </summary>
    public string? ImagePath
    {
        get => (string?)GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    private static void OnImagePathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((AgentAvatar)sender).BeginLoadCustomImage();

    private static void OnPaletteInputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((AgentAvatar)sender).ApplyPalette();

    /// <summary>
    /// Selects the identity colour behind the fallback initial, or clears it while a custom image
    /// is showing. Re-runs whenever the inputs change or the control switches face, so an image
    /// that fails to load later still lands on the agent's own colour rather than a neutral one.
    /// </summary>
    private void ApplyPalette()
    {
        string state;
        if (_showingCustomImage || (PaletteIndex < 0 && string.IsNullOrWhiteSpace(Initial)))
        {
            state = "Neutral";
        }
        else
        {
            var index = PaletteIndex < 0
                ? AvatarPalette.From(Initial)
                : PaletteIndex % PaletteStateNames.Length;

            // A caller passing a negative index other than "derive it" would otherwise index
            // backwards off the array.
            if (index < 0) index += PaletteStateNames.Length;
            state = PaletteStateNames[index];
        }

        VisualStateManager.GoToState(this, state, useTransitions: false);
    }

    private void BeginLoadCustomImage()
    {
        var loadVersion = Interlocked.Increment(ref _imageLoadVersion);
        var configuredPath = ImagePath;

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            ShowInitial();
            return;
        }

        var absolutePath = ResolveManagedImagePath(configuredPath);
        if (absolutePath is null)
        {
            ShowInitial();
            return;
        }

        if (ImageCache.TryGetValue(absolutePath, out var cached) && cached.TryGetTarget(out var cachedImage))
        {
            ShowImage(cachedImage);
            return;
        }

        // Deliberately not awaited: the load swallows every failure and falls back to the initial,
        // so there is nothing for a caller to observe.
        _ = LoadImageAsync(absolutePath, loadVersion);
    }

    private async Task LoadImageAsync(string absolutePath, int loadVersion)
    {
        try
        {
            await using var file = File.OpenRead(absolutePath);
            using var stream = file.AsRandomAccessStream();

            var image = new BitmapImage();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth > MaxDecodePixels)
            {
                // Logical rather than Physical so the decode target follows the display scale and
                // the avatar stays sharp on a 150%/200% monitor.
                image.DecodePixelType = DecodePixelType.Logical;
                image.DecodePixelWidth = MaxDecodePixels;
            }

            stream.Seek(0);
            await image.SetSourceAsync(stream);

            // The path can change while the file is being read. Applying a stale result would put
            // one agent's picture on another's row.
            if (loadVersion != Volatile.Read(ref _imageLoadVersion)) return;

            Cache(absolutePath, image);
            ShowImage(image);
        }
        catch (Exception)
        {
            // Missing, corrupt, locked or unsupported: the initial is the safe fallback, and an
            // agent list must not break because one file went bad.
            if (loadVersion == Volatile.Read(ref _imageLoadVersion)) ShowInitial();
        }
    }

    /// <summary>
    /// Turns a stored relative path (or an uncommitted absolute preview path) into a file this
    /// control is allowed to open, or null if it points outside the managed directories.
    /// </summary>
    private static string? ResolveManagedImagePath(string configuredPath)
    {
        try
        {
            var trimmedPath = configuredPath.Trim();
            if (!Path.IsPathFullyQualified(trimmedPath))
            {
                return AppStorage.TryGetAgentIconAbsolutePath(trimmedPath, out var resolved)
                    ? resolved
                    : null;
            }

            var absolutePath = Path.GetFullPath(trimmedPath);
            return AppStorage.IsPathInsideDirectory(absolutePath, AppStorage.AgentIconsDir)
                   || AppStorage.IsPathInsideDirectory(absolutePath, AppStorage.TemporaryAgentIconsDir)
                ? absolutePath
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException
                                             or NotSupportedException
                                             or PathTooLongException)
        {
            return null;
        }
    }

    private static void Cache(string absolutePath, BitmapImage image)
    {
        var isNewPath = !ImageCache.ContainsKey(absolutePath);
        ImageCache[absolutePath] = new WeakReference<BitmapImage>(image);
        if (isNewPath) ImageCacheOrder.Enqueue(absolutePath);

        while (ImageCacheOrder.Count > MaxCachedImages && ImageCacheOrder.TryDequeue(out var evicted))
        {
            ImageCache.Remove(evicted);
        }
    }

    private void ShowImage(BitmapImage image)
    {
        CustomAvatarImage.Source = image;
        CustomAvatarImage.Visibility = Visibility.Visible;
        InitialText.Visibility = Visibility.Collapsed;

        _showingCustomImage = true;
        ApplyPalette();
    }

    private void ShowInitial()
    {
        CustomAvatarImage.Source = null;
        CustomAvatarImage.Visibility = Visibility.Collapsed;
        InitialText.Visibility = Visibility.Visible;

        _showingCustomImage = false;
        ApplyPalette();
    }

    /// <summary>
    /// A decode can also fail after the source is attached, which never reaches the load's own
    /// catch. Invalidate first so an in-flight load cannot put the broken image straight back.
    /// </summary>
    private void CustomImage_ImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        Interlocked.Increment(ref _imageLoadVersion);
        ShowInitial();
    }
}
