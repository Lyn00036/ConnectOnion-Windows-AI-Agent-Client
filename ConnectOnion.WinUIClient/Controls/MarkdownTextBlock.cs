using ConnectOnion.WinUIClient.Rendering;
using Markdig;
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
        ActualThemeChanged += (_, _) => RenderMarkdown(Markdown);
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

        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var renderer = new WinUiMarkdownRenderer(_richTextBlock, SearchQuery, ActiveMatchIndex);
        Pipeline.Setup(renderer);
        renderer.Render(document);

        if (_richTextBlock.Blocks.Count == 0)
        {
            var fallback = new Paragraph();
            fallback.Inlines.Add(new Run { Text = markdown });
            _richTextBlock.Blocks.Add(fallback);
        }
    }
}
