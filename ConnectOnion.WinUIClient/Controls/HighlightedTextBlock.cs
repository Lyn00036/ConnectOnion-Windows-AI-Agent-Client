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

        AddHighlightedRuns(
            paragraph.Inlines, Text ?? string.Empty, SearchQuery ?? string.Empty, ActiveMatchIndex,
            fontSize: _textBlock.FontSize);
    }

    /// <summary>Rough descender height as a fraction of the font size (Segoe UI is ~0.21 em).</summary>
    private const double DescentRatio = 0.22;

    /// <summary>
    /// An <see cref="InlineUIContainer"/> aligns the <i>bottom edge</i> of its child with the
    /// surrounding text's <i>baseline</i> — not with the bottom of the text — so anything hosted in
    /// one sits a descender too high and visibly floats above the line. Push it back down by that
    /// descent, plus whatever padding sits beneath the child's own text.
    ///
    /// This is a <see cref="RenderTransform"/> on purpose: it shifts the element without changing
    /// the line box, so re-aligning a highlight can't reflow the paragraph it sits in.
    /// </summary>
    internal static void ApplyBaselineNudge(FrameworkElement element, double fontSize, double bottomPadding = 0)
        => element.RenderTransform = new TranslateTransform
        {
            Y = Math.Round(fontSize * DescentRatio + bottomPadding),
        };

    /// <summary>
    /// Appends <paramref name="text"/> to <paramref name="inlines"/>, wrapping each occurrence of
    /// <paramref name="query"/> in a highlight.
    ///
    /// <paramref name="allowInlineUIContainers"/> must be false when the target collection is not
    /// a <see cref="Paragraph"/> inside a <see cref="RichTextBlock"/>: an
    /// <see cref="InlineUIContainer"/> is only legal there, and both <c>TextBlock.Inlines</c> and
    /// <c>Hyperlink.Inlines</c> throw <c>ArgumentException("Value does not fall within the
    /// expected range")</c> on one. Those targets get a bold/underlined <see cref="Run"/> instead
    /// — no background fill, but the match is still visible and still counted, so find navigation
    /// stays in step with the highlights that do get a fill.
    /// </summary>
    internal static void AddHighlightedRuns(
        InlineCollection inlines,
        string text,
        string query,
        int activeMatchIndex,
        ref int matchIndex,
        bool allowInlineUIContainers = true,
        double fontSize = 14)
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
            var matched = text.Substring(index, query.Length);

            if (!allowInlineUIContainers)
            {
                inlines.Add(new Underline
                {
                    Inlines =
                    {
                        new Run
                        {
                            Text = matched,
                            FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold,
                        },
                    },
                });
                matchIndex++;
                cursor = index + query.Length;
                continue;
            }

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
                Text = matched,
                // Match the surrounding text's size, or the highlighted word renders at the
                // TextBlock default (14) and jumps out of line with the prose around it.
                FontSize = fontSize,
                Foreground = (Brush)Application.Current.Resources["SearchMatchTextBrush"],
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
            };

            border.Child = matchText;
            ApplyBaselineNudge(border, fontSize);
            inlines.Add(new InlineUIContainer { Child = border });
            matchIndex++;
            cursor = index + query.Length;
        }
    }

    private static void AddHighlightedRuns(
        InlineCollection inlines, string text, string query, int activeMatchIndex, double fontSize)
    {
        var matchIndex = 0;
        AddHighlightedRuns(inlines, text, query, activeMatchIndex, ref matchIndex, fontSize: fontSize);
    }
}
