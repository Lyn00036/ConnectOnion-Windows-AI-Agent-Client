using System;
using System.Numerics;
using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class InteractiveCard : UserControl
{
    public InteractiveCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public ChatMessage Message
    {
        get => (ChatMessage)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(ChatMessage), typeof(InteractiveCard), new PropertyMetadata(null));

    public string IconGlyph { get => (string)GetValue(IconGlyphProperty); set => SetValue(IconGlyphProperty, value); }
    public static readonly DependencyProperty IconGlyphProperty = DependencyProperty.Register(
        nameof(IconGlyph), typeof(string), typeof(InteractiveCard), new PropertyMetadata("\uE946"));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(InteractiveCard), new PropertyMetadata(""));

    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(InteractiveCard), new PropertyMetadata(""));

    public string StatusLabel { get => (string)GetValue(StatusLabelProperty); set => SetValue(StatusLabelProperty, value); }
    public static readonly DependencyProperty StatusLabelProperty = DependencyProperty.Register(
        nameof(StatusLabel), typeof(string), typeof(InteractiveCard), new PropertyMetadata(""));

    public string HeaderMeta { get => (string)GetValue(HeaderMetaProperty); set => SetValue(HeaderMetaProperty, value); }
    public static readonly DependencyProperty HeaderMetaProperty = DependencyProperty.Register(
        nameof(HeaderMeta), typeof(string), typeof(InteractiveCard), new PropertyMetadata(""));

    public bool ShowStatusBadge { get => (bool)GetValue(ShowStatusBadgeProperty); set => SetValue(ShowStatusBadgeProperty, value); }
    public static readonly DependencyProperty ShowStatusBadgeProperty = DependencyProperty.Register(
        nameof(ShowStatusBadge), typeof(bool), typeof(InteractiveCard), new PropertyMetadata(true));

    public bool ShowBody { get => (bool)GetValue(ShowBodyProperty); set => SetValue(ShowBodyProperty, value); }
    public static readonly DependencyProperty ShowBodyProperty = DependencyProperty.Register(
        nameof(ShowBody), typeof(bool), typeof(InteractiveCard), new PropertyMetadata(true));

    public bool ShowFooter { get => (bool)GetValue(ShowFooterProperty); set => SetValue(ShowFooterProperty, value); }
    public static readonly DependencyProperty ShowFooterProperty = DependencyProperty.Register(
        nameof(ShowFooter), typeof(bool), typeof(InteractiveCard), new PropertyMetadata(true));

    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(InteractiveCard), new PropertyMetadata(false));

    public InteractiveVisualTone ChromeTone { get => (InteractiveVisualTone)GetValue(ChromeToneProperty); set => SetValue(ChromeToneProperty, value); }
    public static readonly DependencyProperty ChromeToneProperty = DependencyProperty.Register(
        nameof(ChromeTone), typeof(InteractiveVisualTone), typeof(InteractiveCard),
        new PropertyMetadata(InteractiveVisualTone.Neutral, OnChromeToneChanged));

    /// <summary>
    /// Whether the left tone rail is drawn at all.
    ///
    /// <para>The rail exists to signal a tone — amber while an approval is pending, red on a
    /// rejection. <see cref="InteractiveVisualTone.Neutral"/> means there is nothing to signal,
    /// but the tone brush for Neutral is <c>BorderDefaultBrush</c>, so the rail used to paint
    /// itself in exactly the card's own border colour. On a settled card that read as a stray
    /// thick grey edge that matched none of the other three sides and got sliced by the corner
    /// radius — an artifact, not a signal. A settled card already says what it is through its
    /// icon and outcome text.</para>
    /// </summary>
    public bool ShowChromeRail { get => (bool)GetValue(ShowChromeRailProperty); private set => SetValue(ShowChromeRailProperty, value); }
    public static readonly DependencyProperty ShowChromeRailProperty = DependencyProperty.Register(
        nameof(ShowChromeRail), typeof(bool), typeof(InteractiveCard), new PropertyMetadata(false));

    private static void OnChromeToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((InteractiveCard)d).ShowChromeRail =
            (InteractiveVisualTone)e.NewValue != InteractiveVisualTone.Neutral;

    public InteractiveVisualTone IconTone { get => (InteractiveVisualTone)GetValue(IconToneProperty); set => SetValue(IconToneProperty, value); }
    public static readonly DependencyProperty IconToneProperty = DependencyProperty.Register(
        nameof(IconTone), typeof(InteractiveVisualTone), typeof(InteractiveCard), new PropertyMetadata(InteractiveVisualTone.Neutral));

    public string AccessibilityName { get => (string)GetValue(AccessibilityNameProperty); set => SetValue(AccessibilityNameProperty, value); }
    public static readonly DependencyProperty AccessibilityNameProperty = DependencyProperty.Register(
        nameof(AccessibilityName), typeof(string), typeof(InteractiveCard), new PropertyMetadata("Interactive card"));

    public object? CardBody { get => GetValue(CardBodyProperty); set => SetValue(CardBodyProperty, value); }
    public static readonly DependencyProperty CardBodyProperty = DependencyProperty.Register(
        nameof(CardBody), typeof(object), typeof(InteractiveCard), new PropertyMetadata(null));

    public object? CardFooter { get => GetValue(CardFooterProperty); set => SetValue(CardFooterProperty, value); }
    public static readonly DependencyProperty CardFooterProperty = DependencyProperty.Register(
        nameof(CardFooter), typeof(object), typeof(InteractiveCard), new PropertyMetadata(null));

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Message is null || Message.HasPlayedInteractiveEntrance) return;
        Message.HasPlayedInteractiveEntrance = true;
        if (!new UISettings().AnimationsEnabled) return;

        CardBorder.Opacity = 0;
        CardBorder.Translation = new Vector3(0, 4, 0);
        CardBorder.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(140) };
        CardBorder.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(140) };
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            CardBorder.Opacity = 1;
            CardBorder.Translation = Vector3.Zero;
        });
    }
}
