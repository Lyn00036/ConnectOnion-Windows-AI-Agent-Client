using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// A horizontal "icon + label" pair, the app's standard button content. Icon
/// and text pick up the inherited <see cref="Control.Foreground"/> and
/// <see cref="Control.FontSize"/>; <see cref="Glyph"/>, <see cref="Text"/>,
/// <see cref="IconSize"/> and <see cref="Spacing"/> are set per site.
/// </summary>
public sealed partial class IconText : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            nameof(Glyph),
            typeof(Icon),
            typeof(IconText),
            new PropertyMetadata(default(Icon)));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(IconText),
            new PropertyMetadata(""));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(
            nameof(IconSize),
            typeof(IconSize),
            typeof(IconText),
            new PropertyMetadata(FluentIcons.Common.IconSize.Size16));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(IconText),
            new PropertyMetadata(8.0));

    public IconText()
    {
        InitializeComponent();
    }

    public Icon Glyph
    {
        get => (Icon)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IconSize IconSize
    {
        get => (IconSize)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }
}
