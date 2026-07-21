using ConnectOnion.WinUIClient.Rendering;
using Markdig;
using Markdig.Syntax;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// Renders markdown into a WinUI <see cref="RichTextBlock"/> using Markdig and a custom renderer.
/// </summary>
public sealed class MarkdownTextBlock : UserControl
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private readonly RichTextBlock _richTextBlock;
    // Parse cache. Re-rendering is common and cheap-ish; re-*parsing* is not, and the two most
    // frequent triggers (a find-query keystroke, a theme flip) leave the markdown itself
    // unchanged. Keyed on the source string so a genuine content change still re-parses.
    private MarkdownDocument? _parsedDocument;
    private string _parsedMarkdown = string.Empty;

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown),
            typeof(string),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public static readonly DependencyProperty MaxLinesProperty =
        DependencyProperty.Register(
            nameof(MaxLines),
            typeof(int),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(0, OnMaxLinesChanged));

    public static readonly DependencyProperty SearchQueryProperty =
        DependencyProperty.Register(
            nameof(SearchQuery),
            typeof(string),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(string.Empty, OnRenderPropertyChanged));

    public static readonly DependencyProperty ActiveMatchIndexProperty =
        DependencyProperty.Register(
            nameof(ActiveMatchIndex),
            typeof(int),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(-1, OnRenderPropertyChanged));

    public MarkdownTextBlock()
    {
        _richTextBlock = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords,
        };

        // Foreground/FontSize/MaxLines are bound rather than copied because this is a
        // UserControl wrapping a RichTextBlock: those properties are set on the *outer* control
        // by callers and XAML styles, and nothing would otherwise carry them to the inner one.
        _richTextBlock.SetBinding(RichTextBlock.ForegroundProperty, new Binding
        {
            Path = new PropertyPath(nameof(Foreground)),
            Source = this,
        });
        _richTextBlock.SetBinding(RichTextBlock.FontSizeProperty, new Binding
        {
            Path = new PropertyPath(nameof(FontSize)),
            Source = this,
        });
        _richTextBlock.SetBinding(RichTextBlock.MaxLinesProperty, new Binding
        {
            Path = new PropertyPath(nameof(MaxLines)),
            Source = this,
        });

        Content = _richTextBlock;
        // A theme flip has to re-render, not just re-bind: the renderer bakes brushes into the
        // inlines it produces (code spans, links, search highlights), so those colors are
        // already fixed by the time they reach the tree. The parse cache makes this cheap.
        ActualThemeChanged += (_, _) => RenderMarkdown(Markdown);
        // Same reasoning for font size: the renderer bakes absolute sizes into headings and code
        // spans from the base FontSize, so a size change (the "Message text size" setting, applied
        // live or stamped after the markdown first renders) needs a re-render, not just the inner
        // RichTextBlock's bound FontSize update — otherwise those inlines keep the old size.
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => RenderMarkdown(Markdown));
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public int MaxLines
    {
        get => (int)GetValue(MaxLinesProperty);
        set => SetValue(MaxLinesProperty, value);
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

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MarkdownTextBlock)d).RenderMarkdown(e.NewValue as string ?? string.Empty);

    private static void OnMaxLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MarkdownTextBlock)d)._richTextBlock.MaxLines = (int)e.NewValue;

    private static void OnRenderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MarkdownTextBlock)d).RenderMarkdown(((MarkdownTextBlock)d).Markdown);

    private void RenderMarkdown(string markdown)
    {
        _richTextBlock.Blocks.Clear();

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        if (_parsedDocument is null || !string.Equals(_parsedMarkdown, markdown, System.StringComparison.Ordinal))
        {
            _parsedDocument = Markdig.Markdown.Parse(markdown, Pipeline);
            _parsedMarkdown = markdown;
        }

        // A fresh renderer per render: it carries the search query and active match as state and
        // writes into the block collection, so it is single-use by construction.
        var renderer = new WinUiMarkdownRenderer(_richTextBlock, SearchQuery, ActiveMatchIndex);
        Pipeline.Setup(renderer);
        renderer.Render(_parsedDocument);

        // Safety net: markdown that parses to nothing renderable (an unsupported construct, or
        // one the custom renderer has no visitor for) would otherwise show as an empty bubble
        // and silently lose the agent's reply. Falling back to the raw text is always better.
        if (_richTextBlock.Blocks.Count == 0)
        {
            var fallback = new Paragraph();
            fallback.Inlines.Add(new Run { Text = markdown });
            _richTextBlock.Blocks.Add(fallback);
        }
    }
}
