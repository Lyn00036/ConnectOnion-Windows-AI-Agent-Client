using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// A two-column settings/detail row: bold <see cref="Title"/> + caption
/// <see cref="Description"/> on the left, and the row's XAML child content
/// (<see cref="RightContent"/>) on the right. Any per-site width lives on the
/// injected control itself — the right column is always Auto.
/// </summary>
[ContentProperty(Name = nameof(RightContent))]
public sealed partial class SettingRow : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(SettingRow),
            new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(SettingRow),
            new PropertyMetadata(""));

    public static readonly DependencyProperty RightContentProperty =
        DependencyProperty.Register(
            nameof(RightContent),
            typeof(object),
            typeof(SettingRow),
            new PropertyMetadata(null));

    public SettingRow()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }
}
