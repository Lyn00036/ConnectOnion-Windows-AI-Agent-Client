using System;
using System.Numerics;
using ConnectOnion.WinUIClient.Services;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// The small animated starburst beside "Thinking…". Eight rounded rays breathe, drift and fade
/// out of phase with each other, so the mark reads as ongoing thought rather than as a progress
/// spinner — nothing about it implies a percentage or an ETA.
///
/// Deliberately not a <see cref="ProgressRing"/>: a determinate-looking spinner in a chat
/// transcript invites "how far along is it?", which is a question this state cannot answer.
///
/// <para><b>Lifetime.</b> A <see cref="CanvasAnimatedControl"/> ticks by default and keeps ticking
/// after its page is navigated away from, because this app caches pages
/// (<c>NavigationCacheMode.Required</c>) — the control is still loaded, just not on screen. So the
/// canvas starts paused and only runs while <see cref="IsActive"/> is true and the control is
/// loaded; <c>Unloaded</c> pauses unconditionally, which covers both page navigation and the
/// virtualizing list recycling the container. Start/Stop are idempotent because
/// <c>Loaded</c>/<c>Unloaded</c> fire repeatedly on the same instance as the list recycles.</para>
///
/// <para><b>Cost.</b> One loop runs per visible thinking row, and the projection only ever keeps
/// one of those alive at a time (see <c>ChatTurnProjection.SettleLlmThinking</c>), so a
/// conversation with a hundred past turns animates nothing. Brushes and stroke styles are created
/// once per device rather than per frame — see <see cref="EnsureResources"/>.</para>
/// </summary>
public sealed partial class ThinkingIndicator : UserControl, IDisposable
{
    // ---- Geometry (device-independent pixels, within an 18x18 box) ----
    private const int RayCount = 8;
    private const float BaseRayLength = 4.0f;
    private const float RayInnerRadius = 2.4f;
    private const float RayThickness = 1.8f;

    // ---- Motion ----
    // The three periods are deliberately not multiples of one another: rotation, ray pulse and
    // breathing drift in and out of alignment instead of resetting together, which is what keeps
    // a short loop from reading as a loop.
    private const double RotationPeriodSeconds = 2.7;
    private const double RayPulsePeriodSeconds = 1.3;
    private const double BreathingPeriodSeconds = 2.2;

    // Fraction of a full pulse cycle each successive ray lags behind the previous one. 0.5/RayCount
    // walks the bright spot around the ring twice per pulse, so neighbouring rays never move in
    // lockstep and the whole ring never dims at once.
    private const double RayPhaseOffset = 0.5 / RayCount;

    private const float LengthAmplitude = 0.20f;   // ±20% → the 75%..115% band the design asks for
    private const float LengthBias = 0.95f;
    private const float RadialDriftAmplitude = 0.35f;
    private const float MinOpacity = 0.35f;
    private const float MaxOpacity = 1.0f;
    private const float BreathingAmplitude = 0.04f;

    /// <summary>Read once: the system exposes this as a user setting that requires a restart of
    /// the app's animations to observe, and the app already treats it as a startup constant (see
    /// <c>DisclosureAnimation</c>).</summary>
    private static readonly bool AnimationsEnabled = new UISettings().AnimationsEnabled;

    private CanvasStrokeStyle? _strokeStyle;
    private Color _rayColor;
    private bool _isRunning;
    private bool _isUnloaded;
    private int _disposed;

    public ThinkingIndicator()
    {
        InitializeComponent();
        ApplyThemeColor();
        // Paused from the start. CanvasAnimatedControl runs by default, and this template is
        // realized for every activity row — including the ones that are not thinking — so without
        // this each one would spin up a render thread before ApplyActiveState ever ran.
        Canvas.Paused = true;
        Canvas.Draw += Canvas_Draw;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    /// <summary>
    /// Whether the model is currently working. True shows the mark and runs the loop; false
    /// collapses the control and retires the loop. Bound straight to the message's existing
    /// running state — this control introduces no state of its own to keep in sync.
    /// </summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(ThinkingIndicator),
        new PropertyMetadata(false, OnIsActiveChanged));

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ThinkingIndicator)d).ApplyActiveState();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        ApplyActiveState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Unloaded is the one signal that reliably precedes a recycled list item being reused or
        // a page being navigated away from, so it stops unconditionally — regardless of IsActive,
        // which may still read true for a turn that is genuinely still running elsewhere.
        _isUnloaded = true;
        Stop();

        // Pause only — deliberately no RemoveFromVisualTree here. Unloaded is not the end of this
        // instance: a ListView row is unloaded and loaded again as it scrolls in and out, and
        // removing the canvas the first time left an empty box on every reload (there is no
        // documented way to re-attach one). The leak this was reaching for is already handled by
        // x:Load in ChatPage.xaml, which stops a canvas being built for rows that are not
        // thinking — that bounds the count at one, which is the part that actually mattered.
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => ApplyThemeColor();

    private void ApplyActiveState()
    {
        if (IsActive && !_isUnloaded)
        {
            Visibility = Visibility.Visible;
            Start();
        }
        else
        {
            Stop();
            Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Runs the loop. Pausing rather than detaching is deliberate: this control lives in a
    /// <see cref="ListView"/> item, and a virtualizing list recycles its containers — Unloaded and
    /// Loaded fire over and over on the same instance as the user scrolls. Re-attaching a
    /// <see cref="CanvasAnimatedControl"/> after <c>RemoveFromVisualTree</c> is not a path Win2D
    /// documents as supported, so the hot path stays on <see cref="CanvasAnimatedControl.Paused"/>,
    /// which is what actually stops Update/Draw from ticking. Teardown of the underlying resources
    /// is left to the control's own disposal with the item.
    /// </summary>
    private void Start()
    {
        if (_isRunning) return;
        // Reduce Motion still gets the mark, just held still: the meaning is carried by the
        // "Thinking…" text beside it, so suppressing the motion costs nothing but the motion.
        // A paused canvas keeps its last drawn frame, so this is a static starburst, not a gap.
        Canvas.Paused = !AnimationsEnabled;
        _isRunning = true;
    }

    private void Stop()
    {
        if (!_isRunning) return;
        Canvas.Paused = true;
        _isRunning = false;
    }

    /// <summary>Releases the Win2D swap chain when the containing chat page is discarded.</summary>
    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Stop();
        Canvas.Draw -= Canvas_Draw;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ActualThemeChanged -= OnActualThemeChanged;
        _strokeStyle?.Dispose();
        _strokeStyle = null;
        Canvas.RemoveFromVisualTree();
    }

    private void ApplyThemeColor()
    {
        var isDark = ActualTheme == ElementTheme.Dark
                     || (ActualTheme == ElementTheme.Default
                         && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        // The same token the "Thinking…" text uses, so the mark sits at the text's weight rather
        // than competing with it. Read through ThemeService because Win2D needs a raw Color, not
        // a Brush, and this keeps the hex out of the code (see the colour-system notes in
        // CLAUDE.md).
        _rayColor = ThemeService.GetColor("TextSecondaryColor", isDark);

        // The canvas is cleared to the transcript's own background rather than to Transparent.
        // CanvasAnimatedControl draws through a swap chain, and a transparent clear leaves that
        // surface composited against whatever the driver happens to give it — which shows up as a
        // pale box in the dark theme and a dim one in the light. Painting the same colour the row
        // sits on makes the 18x18 square invisible either way. It has to be re-read on a theme
        // flip for the same reason the ray colour does, hence both living here.
        //
        // This is the token ChatPage's root Grid uses (AppBackgroundBrush); if a thinking row ever
        // moves onto an elevated surface, this has to move with it or the square will show.
        Canvas.ClearColor = ThemeService.GetColor("AppBackgroundColor", isDark);
    }

    private void EnsureResources()
    {
        // Created once and reused for the life of the control. A CanvasStrokeStyle allocated per
        // frame is 60 objects a second for a mark 18 pixels across.
        _strokeStyle ??= new CanvasStrokeStyle
        {
            StartCap = CanvasCapStyle.Round,
            EndCap = CanvasCapStyle.Round,
        };
    }

    private void Canvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        var width = (float)sender.Size.Width;
        var height = (float)sender.Size.Height;
        if (width <= 0 || height <= 0) return;

        EnsureResources();

        // Elapsed time, not a frame counter: the mark then runs at the same speed whether the
        // render thread is keeping up or not.
        var elapsed = args.Timing.TotalTime.TotalSeconds;
        var centre = new Vector2(width / 2f, height / 2f);

        var rotation = (float)(elapsed / RotationPeriodSeconds * 2 * Math.PI);
        var breathing = 1f + (float)Math.Sin(elapsed / BreathingPeriodSeconds * 2 * Math.PI) * BreathingAmplitude;

        var ds = args.DrawingSession;
        for (var i = 0; i < RayCount; i++)
        {
            var angle = rotation + (float)(i * 2 * Math.PI / RayCount);

            // Each ray reads the same pulse a little later than its neighbour, so length and
            // opacity travel around the ring instead of the whole mark throbbing as one.
            var pulse = Math.Sin((elapsed / RayPulsePeriodSeconds + i * RayPhaseOffset) * 2 * Math.PI);

            var lengthScale = LengthBias + (float)pulse * LengthAmplitude;
            var inner = (RayInnerRadius + (float)pulse * RadialDriftAmplitude) * breathing;
            var outer = inner + BaseRayLength * lengthScale * breathing;

            // sin is -1..1; the mark should never fully disappear, hence the floor at MinOpacity.
            var normalized = (float)((pulse + 1) / 2);
            var opacity = MinOpacity + normalized * (MaxOpacity - MinOpacity);

            var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            var colour = _rayColor;
            colour.A = (byte)(_rayColor.A * opacity);

            ds.DrawLine(
                centre + direction * inner,
                centre + direction * outer,
                colour,
                RayThickness,
                _strokeStyle);
        }
    }
}
