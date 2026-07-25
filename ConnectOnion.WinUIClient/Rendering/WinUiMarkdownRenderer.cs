using System;
using System.Collections.Generic;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Text;
using ConnectOnion.WinUIClient.Controls;
using ConnectOnion.WinUIClient.Services;

namespace ConnectOnion.WinUIClient.Rendering;

/// <summary>
/// A small Markdig renderer that maps common markdown nodes to WinUI document elements.
/// </summary>
public sealed partial class WinUiMarkdownRenderer : RendererBase
{
    /// <summary>The monospace face used for inline code, fenced blocks, and any search highlight
    /// inside them. One instance: it is immutable, and the descent cache in
    /// <see cref="HighlightedTextBlock"/> keys off its <c>Source</c>.</summary>
    private static readonly FontFamily CodeFont =
        new("ms-appx:///Assets/Fonts/JetBrainsMonoNerdFontMono-Regular.ttf#JetBrainsMono NFM");

    private readonly RichTextBlock _target;
    private readonly Stack<InlineCollection> _inlineScopes = new();
    private readonly string _searchQuery;
    private readonly int _activeMatchIndex;
    private int _matchIndex;
    private int _quoteDepth;
    private int _listDepth;

    /// <summary>Greater than zero while the current inline scope sits inside an element that
    /// cannot host an <see cref="InlineUIContainer"/> — today that means a <see cref="Hyperlink"/>,
    /// which (unlike a Paragraph of the RichTextBlock) accepts only text inlines and throws
    /// <c>ArgumentException</c> on anything else. It is a depth, not a flag, because everything
    /// nested further inside — emphasis spans, inline code, search highlights — is blocked too.</summary>
    private int _uiContainerBlockedDepth;

    private bool CanHostUIContainer => _uiContainerBlockedDepth == 0;

    public WinUiMarkdownRenderer(RichTextBlock target, string searchQuery = "", int activeMatchIndex = -1)
    {
        _target = target;
        _searchQuery = searchQuery;
        _activeMatchIndex = activeMatchIndex;

        ObjectRenderers.Add(new DocumentRenderer());
        ObjectRenderers.Add(new HeadingRenderer());
        ObjectRenderers.Add(new ParagraphRenderer());
        ObjectRenderers.Add(new QuoteBlockRenderer());
        ObjectRenderers.Add(new ListBlockRenderer());
        ObjectRenderers.Add(new FencedCodeBlockRenderer());
        ObjectRenderers.Add(new CodeBlockRenderer());
        ObjectRenderers.Add(new ThematicBreakRenderer());
        ObjectRenderers.Add(new TableBlockRenderer());
        ObjectRenderers.Add(new HtmlBlockRenderer());

        ObjectRenderers.Add(new LiteralInlineRenderer());
        ObjectRenderers.Add(new LineBreakInlineRenderer());
        ObjectRenderers.Add(new CodeInlineRenderer());
        ObjectRenderers.Add(new EmphasisInlineRenderer());
        ObjectRenderers.Add(new LinkInlineRenderer());
        ObjectRenderers.Add(new ContainerInlineRenderer());
        ObjectRenderers.Add(new HtmlInlineRenderer());
    }

    public override object Render(MarkdownObject markdownObject)
    {
        Write(markdownObject);
        return _target;
    }

    internal void WriteDocument(MarkdownDocument document)
    {
        foreach (var block in document)
        {
            Write(block);
        }
    }

    internal void WriteParagraphBlock(ParagraphBlock block, string? prefix = null)
    {
        var paragraph = CreateParagraph();

        if (!string.IsNullOrEmpty(prefix))
        {
            // A hanging indent keeps wrapped list text aligned with the content instead of the
            // bullet/number. Nested lists get one additional Fluent spacing unit per level.
            var leftIndent = 18 + (Math.Max(0, _listDepth - 1) * 18);
            paragraph.Margin = new Thickness(leftIndent, 2, 0, 2);
            paragraph.TextIndent = -18;
        }

        _target.Blocks.Add(paragraph);

        if (!string.IsNullOrEmpty(prefix))
        {
            paragraph.Inlines.Add(new Run { Text = prefix });
        }

        if (block.Inline is not null)
        {
            WriteInlineContainer(block.Inline, paragraph.Inlines);
        }
    }

    internal void WriteHeadingBlock(HeadingBlock block)
    {
        var paragraph = CreateParagraph();
        paragraph.FontWeight = FontWeights.SemiBold;
        // Heading sizes scale with the message's base font size (the "Message text size" setting)
        // rather than being fixed, so a heading stays proportionally larger than its body text at
        // every step. 14 is the Medium baseline the raw sizes below were picked against.
        var scale = _target.FontSize > 0 ? _target.FontSize / 14.0 : 1.0;
        // Parenthesized on purpose: `switch` binds looser than `*`, so without these parens this
        // reads as `(scale * block.Level) switch …` — matching a scaled level against 1/2/3/4.
        paragraph.FontSize = scale * (block.Level switch
        {
            1 => 24,
            2 => 20,
            3 => 18,
            4 => 16,
            _ => 14,
        });
        paragraph.Margin = new Thickness(0, block.Level <= 2 ? 10 : 8, 0, 6);
        _target.Blocks.Add(paragraph);

        if (block.Inline is not null)
        {
            WriteInlineContainer(block.Inline, paragraph.Inlines);
        }
    }

    internal void WriteQuoteBlock(QuoteBlock block)
    {
        _quoteDepth++;
        try
        {
            foreach (var child in block)
            {
                Write(child);
            }
        }
        finally
        {
            _quoteDepth--;
        }
    }

    internal void WriteListBlock(ListBlock block)
    {
        _listDepth++;
        try
        {
            var itemIndex = 1;
            foreach (var child in block)
            {
                if (child is not ListItemBlock item)
                {
                    continue;
                }

                var prefix = block.IsOrdered
                    ? $"{itemIndex}. "
                    : "\u2022 ";
                itemIndex++;

                WriteListItem(item, prefix);
            }
        }
        finally
        {
            _listDepth--;
        }
    }

    internal void WriteFencedCodeBlock(FencedCodeBlock block)
        => WriteCodeBlockLines(block.Lines.ToString(), GetCodeLanguage(block));

    internal void WriteCodeBlock(CodeBlock block)
        => WriteCodeBlockLines(block.Lines.ToString());

    internal void WriteThematicBreak()
    {
        var paragraph = CreateParagraph();
        paragraph.Margin = new Thickness(0, 8, 0, 8);
        paragraph.Inlines.Add(new Run
        {
            Text = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500",
            Foreground = ResolveBrush("TextFillColorTertiaryBrush"),
        });
        _target.Blocks.Add(paragraph);
    }

    internal void WriteTable(Table table)
    {
        var rows = new List<TableRow>();
        var columnCount = table.ColumnDefinitions?.Count ?? 0;

        foreach (var child in table)
        {
            if (child is TableRow row)
            {
                rows.Add(row);
                var nextColumnIndex = 0;

                foreach (var cellObject in row)
                {
                    if (cellObject is TableCell cell)
                    {
                        var cellColumnIndex = ResolveCellColumnIndex(cell, nextColumnIndex);
                        nextColumnIndex = cellColumnIndex + Math.Max(1, cell.ColumnSpan);
                        columnCount = Math.Max(columnCount, nextColumnIndex);
                    }
                }
            }
        }

        if (rows.Count == 0 || columnCount == 0)
        {
            return;
        }

        var tableGrid = new Grid
        {
            Background = ResolveBrush("SurfaceElevatedBrush"),
        };

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = rows[rowIndex];
            var nextColumnIndex = 0;

            foreach (var cellObject in row)
            {
                if (cellObject is not TableCell cell)
                {
                    continue;
                }

                var columnIndex = Math.Clamp(
                    ResolveCellColumnIndex(cell, nextColumnIndex),
                    0,
                    columnCount - 1);
                var columnSpan = Math.Min(Math.Max(1, cell.ColumnSpan), columnCount - columnIndex);
                var rowSpan = Math.Min(Math.Max(1, cell.RowSpan), rows.Count - rowIndex);
                nextColumnIndex = columnIndex + columnSpan;
                var cellText = CreateTableCellText(table, cell, columnIndex, row.IsHeader);
                var reachesLastColumn = columnIndex + columnSpan >= columnCount;
                var reachesLastRow = rowIndex + rowSpan >= rows.Count;

                var cellBorder = new Border
                {
                    Background = row.IsHeader ? ResolveBrush("SurfaceSecondaryBrush") : null,
                    BorderBrush = ResolveBrush("DividerBrush"),
                    BorderThickness = new Thickness(0, 0, reachesLastColumn ? 0 : 1, reachesLastRow ? 0 : 1),
                    // RichTextBlock's line box reserves more invisible space above the visible
                    // glyphs than below them, while inline-code UI containers are nudged down to
                    // share the prose baseline. Keep the same 12px total body padding but bias it
                    // downward so the visible text, rather than its asymmetric line box, is
                    // optically centered between the row dividers.
                    Padding = row.IsHeader
                        ? new Thickness(10, 7, 10, 7)
                        : new Thickness(10, 2, 10, 10),
                    Child = cellText,
                };

                Grid.SetColumn(cellBorder, columnIndex);
                Grid.SetColumnSpan(cellBorder, columnSpan);
                Grid.SetRow(cellBorder, rowIndex);
                Grid.SetRowSpan(cellBorder, rowSpan);
                tableGrid.Children.Add(cellBorder);
            }
        }

        var tableBorder = new Border
        {
            Background = ResolveBrush("SurfaceElevatedBrush"),
            BorderBrush = ResolveBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Disabled,
                Content = tableGrid,
            },
        };

        // InlineUIContainer is the only way WinUI's RichTextBlock can host a Grid. Keep the
        // scroller horizontal-only so the outer virtualized message list remains the sole owner
        // of vertical scrolling.
        HighlightedTextBlock.ApplyBaselineNudge(tableBorder, _target.FontSize, fontFamily: _target.FontFamily);
        var paragraph = CreateParagraph();
        paragraph.Margin = new Thickness(0, 7, 0, 7);
        paragraph.Inlines.Add(new InlineUIContainer { Child = tableBorder });
        _target.Blocks.Add(paragraph);
    }

    private static int ResolveCellColumnIndex(TableCell cell, int nextColumnIndex)
    {
        // Pipe-table cells can retain the parser's default ColumnIndex instead of a monotonically
        // increasing visual position. Trust an explicit index only when it does not move back over
        // columns already occupied by earlier cells in this row; otherwise continue sequentially.
        return cell.ColumnIndex >= nextColumnIndex ? cell.ColumnIndex : nextColumnIndex;
    }

    private RichTextBlock CreateTableCellText(Table table, TableCell cell, int columnIndex, bool isHeader)
    {
        var cellText = new RichTextBlock
        {
            FontFamily = _target.FontFamily,
            FontSize = _target.FontSize,
            FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = _target.Foreground,
            IsTextSelectionEnabled = _target.IsTextSelectionEnabled,
            MinWidth = 72,
            MaxWidth = 320,
            TextAlignment = GetTableTextAlignment(table, columnIndex),
            TextWrapping = TextWrapping.WrapWholeWords,
            // A neighbouring cell can make the row much taller (for example, multiline prose
            // beside one inline-code path). Do not stretch this RichTextBlock to that height:
            // size it to its own document and center the whole block inside the table cell.
            VerticalAlignment = VerticalAlignment.Center,
        };

        foreach (var descendant in cell)
        {
            if (descendant is ParagraphBlock cellParagraph)
            {
                var paragraph = new Paragraph();
                if (cellParagraph.Inline is not null)
                {
                    WriteInlineContainer(cellParagraph.Inline, paragraph.Inlines);
                }

                cellText.Blocks.Add(paragraph);
            }
        }

        if (cellText.Blocks.Count == 0)
        {
            cellText.Blocks.Add(new Paragraph());
        }

        return cellText;
    }

    private static TextAlignment GetTableTextAlignment(Table table, int columnIndex)
    {
        if (table.ColumnDefinitions is null || columnIndex >= table.ColumnDefinitions.Count)
        {
            return TextAlignment.Left;
        }

        return table.ColumnDefinitions[columnIndex].Alignment switch
        {
            TableColumnAlign.Center => TextAlignment.Center,
            TableColumnAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
    }

    internal void WriteHtmlBlock(HtmlBlock block)
        => WritePlainParagraph(block.Lines.ToString());

    internal void WriteLiteral(LiteralInline inline)
        => WriteHighlightedText(inline.Content.ToString());

    internal void WriteLineBreak(LineBreakInline inline)
        => CurrentInlines.Add(new LineBreak());

    internal void WriteCodeInline(CodeInline inline)
    {
        // Inside a Hyperlink there is no room for the Border-in-an-InlineUIContainer treatment
        // below; fall back to a plain monospace span so `code` in a link still renders as code.
        if (!CanHostUIContainer)
        {
            var codeSpan = new Span
            {
                FontFamily = CodeFont,
                Foreground = ResolveBrush("CodeTextBrush"),
            };
            WriteHighlightedText(
                inline.Content, codeSpan.Inlines, allowInlineUIContainers: false, fontFamily: CodeFont);
            CurrentInlines.Add(codeSpan);
            return;
        }

        // RichTextBlock inlines (Run/Span) can't have a Background, so — same
        // technique as HighlightedTextBlock's search-match spans — wrap a Border
        // in an InlineUIContainer to give inline code a subtle fill distinct from
        // both surrounding prose and fenced code blocks (see WriteCodeBlockLines).
        //
        // The code itself goes in a nested RichTextBlock, not a TextBlock: a search
        // match inside the code is highlighted with its own InlineUIContainer, which
        // only a RichTextBlock paragraph may host.
        var codeText = new RichTextBlock
        {
            FontFamily = CodeFont,
            FontSize = _target.FontSize,
            Foreground = ResolveBrush("CodeTextBrush"),
        };
        var codeParagraph = new Paragraph();
        WriteHighlightedText(inline.Content, codeParagraph.Inlines, fontFamily: CodeFont);
        codeText.Blocks.Add(codeParagraph);

        var border = new Border
        {
            Background = ResolveBrush("InlineCodeBackgroundBrush"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Child = codeText,
        };

        // Drop the chip back onto the text baseline. Here we are aligning the code *text* with the
        // prose text, so the offset is the code font's own descender plus the padding under it.
        HighlightedTextBlock.ApplyBaselineNudge(
            border, _target.FontSize, bottomPadding: 1, fontFamily: CodeFont);
        CurrentInlines.Add(new InlineUIContainer { Child = border });
    }

    internal void WriteEmphasis(EmphasisInline inline)
    {
        Span container = inline.DelimiterChar switch
        {
            '~' => new Span(),
            _ when inline.DelimiterCount >= 2 => new Bold(),
            _ => new Italic(),
        };

        if (inline.DelimiterChar == '~')
        {
            container.FontStyle = FontStyle.Italic;
            container.Foreground = ResolveBrush("TextFillColorSecondaryBrush");
        }

        WriteNestedInlines(inline, container.Inlines);
        CurrentInlines.Add(container);
    }

    internal void WriteLink(LinkInline inline)
    {
        if (inline.IsImage)
        {
            WriteHighlightedText(inline.Url ?? string.Empty);
            return;
        }

        var hyperlink = new Hyperlink
        {
            Foreground = ResolveBrush("TextLinkBrush"),
        };

        var navigateUri = CreateUri(inline.Url);
        if (navigateUri is not null)
        {
            hyperlink.Click += async (_, _) => await Launcher.LaunchUriAsync(navigateUri);
        }

        // A Hyperlink may only contain text inlines, so nothing written inside one — link text,
        // a search highlight, inline code — may use an InlineUIContainer.
        if (inline.FirstChild is not null)
        {
            WriteNestedInlines(inline, hyperlink.Inlines, blocksUIContainers: true);
        }
        else
        {
            WriteHighlightedText(inline.Url ?? string.Empty, hyperlink.Inlines, allowInlineUIContainers: false);
        }

        CurrentInlines.Add(hyperlink);
    }

    internal void WriteContainerInline(ContainerInline inline)
        => WriteNestedInlines(inline, CurrentInlines);

    internal void WriteHtmlInline(HtmlInline inline)
        => WriteHighlightedText(inline.Tag);

    private InlineCollection CurrentInlines => _inlineScopes.Peek();

    private Paragraph CreateParagraph()
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 4),
        };

        if (_quoteDepth > 0)
        {
            paragraph.FontStyle = FontStyle.Italic;
            paragraph.Foreground = ResolveBrush("TextFillColorSecondaryBrush");
            paragraph.Margin = new Thickness(6 + ((_quoteDepth - 1) * 14), 4, 0, 4);
            paragraph.Inlines.Add(new Run
            {
                Text = string.Concat(System.Linq.Enumerable.Repeat("\u258e ", _quoteDepth)),
                Foreground = ResolveBrush("BrandPrimaryBrush"),
            });
        }

        return paragraph;
    }

    private void WriteListItem(ListItemBlock item, string prefix)
    {
        var firstRenderableBlock = true;
        foreach (var child in item)
        {
            switch (child)
            {
                case ParagraphBlock paragraph:
                    WriteParagraphBlock(paragraph, firstRenderableBlock ? prefix : "  ");
                    firstRenderableBlock = false;
                    break;
                case FencedCodeBlock fencedCode:
                    if (firstRenderableBlock)
                    {
                        WritePlainParagraph(prefix.TrimEnd());
                        firstRenderableBlock = false;
                    }
                    WriteFencedCodeBlock(fencedCode);
                    break;
                case CodeBlock codeBlock:
                    if (firstRenderableBlock)
                    {
                        WritePlainParagraph(prefix.TrimEnd());
                        firstRenderableBlock = false;
                    }
                    WriteCodeBlock(codeBlock);
                    break;
                default:
                    if (firstRenderableBlock)
                    {
                        WritePlainParagraph(prefix.TrimEnd());
                        firstRenderableBlock = false;
                    }
                    Write(child);
                    break;
            }
        }
    }

    private void WriteCodeBlockLines(string code, string? language = null)
    {
        // Give fenced/indented code blocks a real background + border so they read
        // as a distinct layer from surrounding prose. WinUI 3's RichTextBlock has no
        // BlockUIContainer (unlike WPF/UWP) and Paragraph itself has no Background,
        // so — same InlineUIContainer trick as the inline-code Border above and
        // HighlightedTextBlock's search-match spans — this drops a single Border
        // into its own paragraph as one inline UI element.
        // Nested RichTextBlock rather than TextBlock, for the same reason as inline code:
        // a highlighted search match is an InlineUIContainer, which a TextBlock rejects.
        var codeText = new RichTextBlock
        {
            FontFamily = CodeFont,
            FontSize = _target.FontSize,
            Foreground = ResolveBrush("CodeTextBrush"),
            TextWrapping = TextWrapping.NoWrap,
        };
        var codeParagraph = new Paragraph();
        WriteHighlightedText(code.TrimEnd(), codeParagraph.Inlines, fontFamily: CodeFont);
        codeText.Blocks.Add(codeParagraph);

        var codeScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Padding = new Thickness(10, 8, 10, 8),
            Content = codeText,
        };

        UIElement codeContent = codeScroller;
        if (!string.IsNullOrEmpty(language))
        {
            var codeGrid = new Grid();
            codeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            codeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var languageHeader = new Border
            {
                Background = ResolveBrush("SurfaceSecondaryBrush"),
                BorderBrush = ResolveBrush("DividerBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 5, 10, 5),
                Child = new TextBlock
                {
                    FontFamily = CodeFont,
                    FontSize = Math.Max(11, _target.FontSize - 2),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ResolveBrush("TextFillColorSecondaryBrush"),
                    Text = language,
                },
            };

            Grid.SetRow(languageHeader, 0);
            Grid.SetRow(codeScroller, 1);
            codeGrid.Children.Add(languageHeader);
            codeGrid.Children.Add(codeScroller);
            codeContent = codeGrid;
        }

        var border = new Border
        {
            Background = ResolveBrush("CodeBackgroundBrush"),
            BorderBrush = ResolveBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = codeContent,
        };

        // The block's own (otherwise empty) line still has a baseline, and the container hangs the
        // border's bottom edge off it — so without this the whole block rides up over its line and
        // the gap beneath it looks too big. This aligns the *box* to the surrounding line, not the
        // text inside it: hence the document's font, and no bottom-padding term.
        HighlightedTextBlock.ApplyBaselineNudge(border, _target.FontSize, fontFamily: _target.FontFamily);

        var paragraph = CreateParagraph();
        paragraph.Margin = new Thickness(0, 6, 0, 6);
        paragraph.Inlines.Add(new InlineUIContainer { Child = border });
        _target.Blocks.Add(paragraph);
    }

    private static string? GetCodeLanguage(FencedCodeBlock block)
    {
        var info = block.Info?.ToString()?.Trim() ?? string.Empty;
        if (info.Length == 0)
        {
            return null;
        }

        var separator = info.IndexOfAny(' ', '\t', '{');
        var language = separator >= 0 ? info[..separator] : info;
        return language.Length == 0 ? null : language;
    }

    private void WritePlainParagraph(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var paragraph = CreateParagraph();
        WriteHighlightedText(text.TrimEnd(), paragraph.Inlines);
        _target.Blocks.Add(paragraph);
    }

    private void WriteHighlightedText(string text)
        => WriteHighlightedText(text, CurrentInlines, CanHostUIContainer);

    /// <summary>Set <paramref name="allowInlineUIContainers"/> to false whenever the target is not
    /// a Paragraph of the RichTextBlock — see <see cref="HighlightedTextBlock.AddHighlightedRuns"/>.
    /// <paramref name="fontFamily"/> is the face the text renders in (the code font inside a code
    /// container, otherwise the document's own); it governs both the highlight's face and how far
    /// it has to drop to sit on the baseline.</summary>
    private void WriteHighlightedText(
        string text, InlineCollection inlines, bool allowInlineUIContainers = true, FontFamily? fontFamily = null)
        => HighlightedTextBlock.AddHighlightedRuns(
            inlines, text, _searchQuery, _activeMatchIndex, ref _matchIndex,
            allowInlineUIContainers, _target.FontSize, fontFamily ?? _target.FontFamily);

    private void WriteInlineContainer(ContainerInline container, InlineCollection inlines)
        => WriteNestedInlines(container, inlines);

    /// <param name="blocksUIContainers">True when <paramref name="targetInlines"/> belongs to an
    /// element that rejects an <see cref="InlineUIContainer"/> (a Hyperlink), so everything written
    /// while this scope is on the stack falls back to text-only highlighting.</param>
    private void WriteNestedInlines(
        ContainerInline container, InlineCollection targetInlines, bool blocksUIContainers = false)
    {
        _inlineScopes.Push(targetInlines);
        if (blocksUIContainers) _uiContainerBlockedDepth++;
        try
        {
        for (Markdig.Syntax.Inlines.Inline? child = container.FirstChild; child is not null; child = child.NextSibling)
        {
            Write(child);
        }
        }
        finally
        {
            if (blocksUIContainers) _uiContainerBlockedDepth--;
            _inlineScopes.Pop();
        }
    }

    private static Uri? CreateUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private SolidColorBrush ResolveBrush(string key)
    {
        var colorKey = key switch
        {
            "TextFillColorSecondaryBrush" => "TextSecondaryColor",
            "TextFillColorTertiaryBrush" => "TextTertiaryColor",
            "TextLinkBrush" => "TextLinkColor",
            "BrandPrimaryBrush" => "BrandPrimaryColor",
            "CodeTextBrush" => "CodeTextColor",
            "InlineCodeBackgroundBrush" => "InlineCodeBackgroundColor",
            "CodeBackgroundBrush" => "CodeBackgroundColor",
            "BorderSubtleBrush" => "BorderSubtleColor",
            "DividerBrush" => "DividerColor",
            "SurfaceElevatedBrush" => "SurfaceElevatedColor",
            "SurfaceSecondaryBrush" => "SurfaceSecondaryColor",
            _ => throw new System.Collections.Generic.KeyNotFoundException(
                $"Markdown theme brush '{key}' has no color-token mapping."),
        };

        var isDark = _target.ActualTheme == ElementTheme.Dark;
        return new SolidColorBrush(ThemeService.GetColor(colorKey, isDark));
    }

}
