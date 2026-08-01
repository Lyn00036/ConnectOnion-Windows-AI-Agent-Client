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

    // What the document tree currently on screen was built from. Parsing is cached above; this
    // guards the far more expensive half — turning the syntax tree into Paragraphs, Borders,
    // nested RichTextBlocks and ScrollViewers. Every one of Markdown/SearchQuery/ActiveMatchIndex
    // funnels into RenderMarkdown, and a find pass writes two of them in quick succession, so
    // without this a single keystroke rebuilt each matching bubble's whole tree twice.
    // _renderedTree is false whenever the tree does not match these (never rendered, released on
    // unload, or invalidated by a theme/font change that is baked into the inlines).
    private bool _renderedTree;
    private string _renderedMarkdown = string.Empty;
    private string _renderedQuery = string.Empty;
    private int _renderedActiveMatchIndex = -1;

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
        // Invalidate first — the markdown and the query have not changed, so the guard in
        // RenderMarkdown would otherwise (correctly, for its own purposes) skip the work.
        ActualThemeChanged += (_, _) => { _renderedTree = false; RenderMarkdown(Markdown); };
        // Same reasoning for font size: the renderer bakes absolute sizes into headings and code
        // spans from the base FontSize, so a size change (the "Message text size" setting, applied
        // live or stamped after the markdown first renders) needs a re-render, not just the inner
        // RichTextBlock's bound FontSize update — otherwise those inlines keep the old size.
        // Same invalidation reasoning as the theme handler above: absolute sizes are baked in.
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => { _renderedTree = false; RenderMarkdown(Markdown); });
        Loaded += (_, _) => RenderMarkdown(Markdown);
        Unloaded += (_, _) => ReleaseDocumentTree();
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
        markdown ??= string.Empty;

        // Nothing that affects the output has changed since the tree on screen was built, so
        // rebuilding it would produce the identical document. Checked before the Clear below,
        // because clearing and rebuilding is exactly the work being avoided.
        if (_renderedTree
            && string.Equals(_renderedMarkdown, markdown, System.StringComparison.Ordinal)
            && string.Equals(_renderedQuery, SearchQuery ?? string.Empty, System.StringComparison.Ordinal)
            && _renderedActiveMatchIndex == ActiveMatchIndex)
        {
            return;
        }

        _richTextBlock.Blocks.Clear();
        _renderedTree = false;

        // Virtualized rows still receive dependency-property updates while detached. Building a
        // native RichTextBlock document at that point only creates an off-screen tree that stays
        // alive in the recycle pool. Loaded renders the current value when the row returns.
        if (!IsLoaded) return;

        _renderedTree = true;
        _renderedMarkdown = markdown;
        _renderedQuery = SearchQuery ?? string.Empty;
        _renderedActiveMatchIndex = ActiveMatchIndex;

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
        // The already-null-coalesced copies the guard above recorded, so this renders exactly the
        // state the guard will later compare against.
        var renderer = new WinUiMarkdownRenderer(_richTextBlock, _renderedQuery, _renderedActiveMatchIndex);
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

    private void ReleaseDocumentTree()
    {
        _richTextBlock.Blocks.Clear();
        _parsedDocument = null;
        _parsedMarkdown = string.Empty;
        // The tree is gone, so the guard must not report it as still matching — otherwise the
        // Loaded handler would skip the rebuild and the row would come back from the recycle
        // pool blank.
        _renderedTree = false;
    }
}
