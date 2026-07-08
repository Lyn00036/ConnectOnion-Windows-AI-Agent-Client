using System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace ConnectOnion.WinUIClient.Controls;

public sealed class HighlightedTextBlock : UserControl
{
    private readonly RichTextBlock _textBlock;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(string.Empty, OnRenderPropertyChanged));

    public static readonly DependencyProperty SearchQueryProperty =
        DependencyProperty.Register(
            nameof(SearchQuery),
            typeof(string),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(string.Empty, OnRenderPropertyChanged));

    public static readonly DependencyProperty ActiveMatchIndexProperty =
        DependencyProperty.Register(
            nameof(ActiveMatchIndex),
            typeof(int),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(-1, OnRenderPropertyChanged));

    public HighlightedTextBlock()
    {
        _textBlock = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
        };

        _textBlock.SetBinding(RichTextBlock.ForegroundProperty, new Binding
        {
            Path = new PropertyPath(nameof(Foreground)),
            Source = this,
        });
        _textBlock.SetBinding(RichTextBlock.FontSizeProperty, new Binding
        {
            Path = new PropertyPath(nameof(FontSize)),
            Source = this,
        });

        Content = _textBlock;

        // Search-highlight colors are theme-aware (Styles/Colors.xaml SearchMatch*/
        // SearchCurrentBackground* tokens), resolved fresh on every render — force a
        // re-render on a live theme flip so visible matches don't stay stuck on the
        // old theme's colors.
        ActualThemeChanged += (_, _) => Render();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string SearchQuery
    {
        get => (string)GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    public int ActiveMatchIndex
    {
        get => (int)GetValue(ActiveMatchIndexProperty);
        set => SetValue(ActiveMatchIndexProperty, value);
    }

    private static void OnRenderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HighlightedTextBlock)d).Render();

    private void Render()
    {
        _textBlock.Blocks.Clear();
        var paragraph = new Paragraph();
        _textBlock.Blocks.Add(paragraph);

        AddHighlightedRuns(paragraph.Inlines, Text ?? string.Empty, SearchQuery ?? string.Empty, ActiveMatchIndex);
    }

    internal static void AddHighlightedRuns(InlineCollection inlines, string text, string query, int activeMatchIndex, ref int matchIndex)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            inlines.Add(new Run { Text = text });
            return;
        }

        var cursor = 0;
        while (cursor < text.Length)
        {
            var index = text.IndexOf(query, cursor, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                inlines.Add(new Run { Text = text[cursor..] });
                return;
            }

            if (index > cursor)
            {
                inlines.Add(new Run { Text = text[cursor..index] });
            }

            var isActive = matchIndex == activeMatchIndex;

            // WinUI 3 inline text elements (Run, Span) lack a Background property.
            // Use InlineUIContainer with a Border to render the highlight background.
            // Resolved fresh (not cached) so this always reflects the current theme —
            // see the ActualThemeChanged handler in the constructor above.
            var border = new Border
            {
                Background = (Brush)Application.Current.Resources[
                    isActive ? "SearchCurrentBackgroundBrush" : "SearchMatchBackgroundBrush"],
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(2, 0, 2, 0),
            };

            var matchText = new TextBlock
            {
                Text = text.Substring(index, query.Length),
                Foreground = (Brush)Application.Current.Resources["SearchMatchTextBrush"],
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
            };

            border.Child = matchText;
            inlines.Add(new InlineUIContainer { Child = border });
            matchIndex++;
            cursor = index + query.Length;
        }
    }

    private static void AddHighlightedRuns(InlineCollection inlines, string text, string query, int activeMatchIndex)
    {
        var matchIndex = 0;
        AddHighlightedRuns(inlines, text, query, activeMatchIndex, ref matchIndex);
    }
}
