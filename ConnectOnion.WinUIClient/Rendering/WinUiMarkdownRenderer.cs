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

namespace ConnectOnion.WinUIClient.Rendering;

/// <summary>
/// A small Markdig renderer that maps common markdown nodes to WinUI document elements.
/// </summary>
public sealed partial class WinUiMarkdownRenderer : RendererBase
{
    /// <summary>The monospace face used for inline code, fenced blocks, and any search highlight
    /// inside them. One instance: it is immutable, and the descent cache in
    /// <see cref="HighlightedTextBlock"/> keys off its <c>Source</c>.</summary>
    private static readonly FontFamily CodeFont = new("Consolas");

    private readonly RichTextBlock _target;
    private readonly Stack<InlineCollection> _inlineScopes = new();
    private readonly string _searchQuery;
    private readonly int _activeMatchIndex;
    private int _matchIndex;
    private int _quoteDepth;

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
        paragraph.FontSize = block.Level switch
        {
            1 => 24,
            2 => 20,
            3 => 18,
            4 => 16,
            _ => 14,
        };
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

    internal void WriteFencedCodeBlock(FencedCodeBlock block)
        => WriteCodeBlockLines(block.Lines.ToString());

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
        foreach (var child in table)
        {
            if (child is not TableRow row)
            {
                continue;
            }

            var paragraph = CreateParagraph();
            paragraph.Margin = new Thickness(0, 2, 0, 2);
            _target.Blocks.Add(paragraph);

            var firstCell = true;
            foreach (var cellObj in row)
            {
                if (cellObj is not TableCell cell)
                {
                    continue;
                }

                if (!firstCell)
                {
                    paragraph.Inlines.Add(new Run
                    {
                        Text = " | ",
                        Foreground = ResolveBrush("TextFillColorSecondaryBrush"),
                    });
                }

                firstCell = false;

                foreach (var descendant in cell)
                {
                    if (descendant is ParagraphBlock cellParagraph && cellParagraph.Inline is not null)
                    {
                        WriteInlineContainer(cellParagraph.Inline, paragraph.Inlines);
                    }
                }
            }
        }
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
            paragraph.Inlines.Add(new Run
            {
                Text = string.Concat(System.Linq.Enumerable.Repeat("\u2502 ", _quoteDepth)),
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

    private void WriteCodeBlockLines(string code)
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

        var border = new Border
        {
            Background = ResolveBrush("CodeBackgroundBrush"),
            BorderBrush = ResolveBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = codeText,
            },
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

    private static SolidColorBrush ResolveBrush(string key)
        => (SolidColorBrush)Application.Current.Resources[key];

}
