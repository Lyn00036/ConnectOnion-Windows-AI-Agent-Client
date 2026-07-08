using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Text.Json;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient;
using ConnectOnion.WinUIClient.Controls;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class ChatPage : Page, IFindHost
{
    /// <summary>Persisted scroll positions keyed by session id, survives page recreation.</summary>
    private static readonly ConcurrentDictionary<string, double> ScrollPositions = new();

    public ChatViewModel Vm { get; } = new();
    private ScrollViewer? _messageScrollViewer;
    private bool _isLoadingConversation;
    private bool _isAutoScrollQueued;
    private bool _stickToBottom = true;
    private ComposerSubmission? _pendingInitialSubmission;
    private readonly List<FindMatch> _findMatches = new();
    private readonly DispatcherTimer _findDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private int _currentFindIndex = -1;
    private string _searchQuery = "";

    public ChatPage()
    {
        InitializeComponent();
        Vm.Messages.CollectionChanged += Messages_CollectionChanged;
        Vm.PropertyChanged += Vm_PropertyChanged;
        _findDebounceTimer.Tick += FindDebounceTimer_Tick;
        ChatRoot.KeyDown += ChatRoot_KeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Persist scroll position so returning to this conversation restores it.
        try
        {
            if (Vm.SessionId is { } sid && _messageScrollViewer is not null)
                ScrollPositions[sid] = _messageScrollViewer.VerticalOffset;
        }
        catch { /* Best-effort scroll persistence. */ }

        Vm.PropertyChanged -= Vm_PropertyChanged;
        _findDebounceTimer.Stop();
        try { Vm.Cleanup(); }
        catch { /* Best-effort cleanup. */ }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Vm.CanSend) or nameof(Vm.IsProcessing))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Composer.CanSubmit = Vm.CanSend;
                Composer.IsBusy = Vm.IsProcessing;
            });
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // AgentDetailPage navigates here with a ComposerSubmission (text + any
        // attachments); a plain string is also accepted for robustness against
        // any other caller that only ever had a prompt to pass.
        _pendingInitialSubmission = e.Parameter switch
        {
            ComposerSubmission submission => submission,
            string prompt => new ComposerSubmission(prompt, Array.Empty<PendingAttachment>()),
            _ => null,
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _messageScrollViewer ??= FindDescendant<ScrollViewer>(MessageList);
        if (_messageScrollViewer is not null)
        {
            _messageScrollViewer.ViewChanged -= MessageScrollViewer_ViewChanged;
            _messageScrollViewer.ViewChanged += MessageScrollViewer_ViewChanged;
        }

        _isLoadingConversation = true;
        try
        {
            await Vm.LoadAsync();
        }
        catch
        {
            // Failed to restore the conversation (e.g. corrupted local
            // storage); the page still loads in an empty state so the user
            // can start a fresh chat.
        }
        finally
        {
            _isLoadingConversation = false;
        }

        // Wait for the ListView to materialize its virtualized item containers
        // before removing the overlay.  A fixed delay cannot account for
        // varying message counts and hardware speed — use ContainerContentChanging
        // to detect when visible items are actually realized.
        _ = DismissOverlayWhenItemsReadyAsync();
        Composer.CanSubmit = Vm.CanSend;
        Composer.IsBusy = Vm.IsProcessing;
        Composer.FocusInput();

        var pending = _pendingInitialSubmission;
        if (pending is not null && (!string.IsNullOrWhiteSpace(pending.Text) || pending.Attachments.Count > 0))
        {
            _pendingInitialSubmission = null;
            await Vm.SendAsync(pending.Text, pending.Attachments);
            QueueScrollToEnd(force: true);
        }
    }

    private async void Composer_SubmitRequested(object? sender, ComposerSubmission submission)
        => await SendAsync(submission.Text, submission.Attachments);

    private async void SubmitOnboard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ChatMessage message)
        {
            await Vm.SubmitOnboardInviteCodeAsync(message);
            ScrollToEnd();
        }
    }

    private async void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChatMessage message ||
            string.IsNullOrEmpty(message.Content))
        {
            return;
        }

        var data = new DataPackage();
        data.SetText(message.Content);
        Clipboard.SetContent(data);

        if (sender is Button button && button.Content is StackPanel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is TextBlock text)
                {
                    var original = text.Text;
                    text.Text = "Copied";
                    await System.Threading.Tasks.Task.Delay(1000);
                    text.Text = original;
                    break;
                }
            }
        }
    }

    private async void AttachmentImage_Tapped(object sender, TappedRoutedEventArgs e)
        => await OpenAttachmentAsync((sender as FrameworkElement)?.Tag as ChatAttachment);

    private async void AttachmentFile_Tapped(object sender, TappedRoutedEventArgs e)
        => await OpenAttachmentAsync((sender as FrameworkElement)?.Tag as ChatAttachment);

    /// <summary>Opens a received/sent attachment in its default OS viewer. Best-effort: a
    /// missing or moved cache file must not crash the chat, just silently no-op.</summary>
    private static async System.Threading.Tasks.Task OpenAttachmentAsync(ChatAttachment? attachment)
    {
        if (attachment is null || string.IsNullOrEmpty(attachment.LocalCachePath)) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(attachment.LocalCachePath);
            await Launcher.LaunchFileAsync(file);
        }
        catch
        {
            // Cache file missing/moved/locked — best-effort open only.
        }
    }

    private async void SaveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ChatAttachment attachment ||
            string.IsNullOrEmpty(attachment.LocalCachePath) || App.MainWindow is null)
        {
            return;
        }

        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var ext = Path.GetExtension(attachment.FileName);
        var extLabel = string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpperInvariant();
        picker.SuggestedFileName = attachment.FileName;
        picker.FileTypeChoices.Add(extLabel, new List<string> { string.IsNullOrEmpty(ext) ? "." : ext });

        var target = await picker.PickSaveFileAsync();
        if (target is null) return;

        try
        {
            using var source = File.OpenRead(attachment.LocalCachePath);
            using var dest = await target.OpenStreamForWriteAsync();
            await source.CopyToAsync(dest);
        }
        catch
        {
            // Best-effort save; the original attachment stays intact either way.
        }
    }

    private void ToggleMessageCollapse_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChatMessage message)
        {
            return;
        }

        message.IsContentExpanded = !message.IsContentExpanded;
    }

    private void ChatRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var isNarrow = e.NewSize.Width < 760;
        Grid.SetRow(ChatHeaderActions, isNarrow ? 1 : 0);
        Grid.SetColumn(ChatHeaderActions, isNarrow ? 0 : 1);
        Grid.SetColumnSpan(ChatHeaderActions, isNarrow ? 2 : 1);
        ChatHeaderActions.Margin = isNarrow ? new Thickness(40, 0, 0, 0) : new Thickness(0);
        ChatHeaderActions.Orientation = isNarrow ? Orientation.Vertical : Orientation.Horizontal;
        ChatHeaderActions.HorizontalAlignment = isNarrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;

        var horizontalInset = e.NewSize.Width < 640 ? 20 : 52;
        MessageList.Padding = new Thickness(horizontalInset, 18, horizontalInset, 22);
    }

    private async System.Threading.Tasks.Task SendAsync(string text, IReadOnlyList<PendingAttachment>? attachments = null)
    {
        var hasAttachments = attachments is { Count: > 0 };
        if ((string.IsNullOrWhiteSpace(text) && !hasAttachments) || !Vm.CanSend) return;

        await Vm.SendAsync(text, attachments);
        QueueScrollToEnd(force: true);
        Composer.FocusInput();
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isLoadingConversation) return;
        if (FindPanel.Visibility == Visibility.Visible)
        {
            QueueFindRefresh();
        }

        if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace))
        {
            return;
        }

        QueueScrollToEnd(force: false);
    }

    private void QueueScrollToEnd(bool force)
    {
        if (Vm.Messages.Count == 0) return;
        if (!force && !_stickToBottom) return;
        if (_isAutoScrollQueued) return;

        _isAutoScrollQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _isAutoScrollQueued = false;
            ScrollToEnd();
        });
    }

    /// <summary>
    /// Waits for the ListView to materialize enough virtualized item containers,
    /// then removes the loading overlay and restores the scroll position.
    /// Uses ContainerContentChanging for precise detection; falls back to a
    /// timeout so the overlay never gets stuck.
    /// </summary>
    private async System.Threading.Tasks.Task DismissOverlayWhenItemsReadyAsync()
    {
        if (Vm.Messages.Count == 0)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        // How many distinct items we need before the viewport is considered
        // populated (typical viewport holds 8–15 items).
        var threshold = Math.Min(Vm.Messages.Count, 12);
        var realized = new HashSet<int>();
        var tcs = new TaskCompletionSource<bool>();

        TypedEventHandler<ListViewBase, ContainerContentChangingEventArgs>? handler = null;
        handler = (_, args) =>
        {
            if (args.ItemIndex >= 0)
                realized.Add(args.ItemIndex);

            if (realized.Count < threshold) return;

            MessageList.ContainerContentChanging -= handler;
            tcs.TrySetResult(true);
        };

        MessageList.ContainerContentChanging += handler;

        // Safety timeout: if the ListView never materializes enough items
        // (e.g. items are zero-height or collapsed), dismiss after 600 ms.
        var timeout = Task.Delay(600);
        await Task.WhenAny(tcs.Task, timeout);
        MessageList.ContainerContentChanging -= handler;

        // Wait one extra dispatcher frame so the RichTextBlock/Markdown
        // content inside each container can finish its internal layout pass
        // before the overlay is removed.
        var frameTcs = new TaskCompletionSource();
        DispatcherQueue.TryEnqueue(() => frameTcs.SetResult());
        await frameTcs.Task;

        try
        {
            // Restore scroll position FIRST (while overlay is still visible),
            // then hide the overlay — this way the scroll jump is invisible.
            if (Vm.SessionId is { } sid
                && ScrollPositions.TryRemove(sid, out var offset)
                && _messageScrollViewer is not null)
            {
                _messageScrollViewer.ChangeView(null, offset, null, disableAnimation: true);
            }
            else
            {
                QueueScrollToEnd(force: true);
            }

            // Allow one more frame for the scroll-restored items to be virtualized.
            var scrollTcs = new TaskCompletionSource();
            DispatcherQueue.TryEnqueue(() => scrollTcs.SetResult());
            await scrollTcs.Task;

            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        catch
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }
    private void ScrollToEnd()
    {
        if (Vm.Messages.Count == 0) return;

        _messageScrollViewer ??= FindDescendant<ScrollViewer>(MessageList);
        if (_messageScrollViewer is not null)
        {
            _messageScrollViewer.ChangeView(null, _messageScrollViewer.ScrollableHeight, null, disableAnimation: true);
            return;
        }

        MessageList.ScrollIntoView(Vm.Messages[^1]);
    }

    private void MessageScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_messageScrollViewer is null) return;

        var distanceFromBottom = _messageScrollViewer.ScrollableHeight - _messageScrollViewer.VerticalOffset;
        _stickToBottom = distanceFromBottom < 96;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }

        return null;
    }

    public void OpenFind()
    {
        FindPanel.Visibility = Visibility.Visible;
        FindTextBox.Focus(FocusState.Programmatic);
        FindTextBox.SelectAll();
        UpdateFindButtons();
    }

    private void CloseFind()
    {
        FindPanel.Visibility = Visibility.Collapsed;
        _findDebounceTimer.Stop();
        _searchQuery = "";
        FindTextBox.Text = "";
        ClearFindHighlights();
        FindNavPanel.Visibility = Visibility.Collapsed;
        FindDivider.Visibility = Visibility.Collapsed;
        FindCounterText.Text = "0 / 0 results";
        UpdateFindButtons();
        MessageList.Focus(FocusState.Programmatic);
    }

    private void ChatRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (FindPanel.Visibility == Visibility.Visible && e.Key == VirtualKey.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = FindTextBox.Text;
        var hasQuery = !string.IsNullOrWhiteSpace(_searchQuery);
        FindNavPanel.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        FindDivider.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        QueueFindRefresh();
    }

    private void FindTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0)
                SelectPreviousMatch();
            else
                SelectNextMatch();

            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void PreviousFind_Click(object sender, RoutedEventArgs e)
        => SelectPreviousMatch();

    private void NextFind_Click(object sender, RoutedEventArgs e)
        => SelectNextMatch();

    private void CloseFind_Click(object sender, RoutedEventArgs e)
        => CloseFind();

    private void QueueFindRefresh()
    {
        _findDebounceTimer.Stop();
        _findDebounceTimer.Start();
    }

    private void FindDebounceTimer_Tick(object? sender, object e)
    {
        _findDebounceTimer.Stop();
        FindAllMatches();
    }

    private void FindAllMatches()
    {
        _findMatches.Clear();
        ClearFindHighlights();

        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            _currentFindIndex = -1;
            FindCounterText.Text = "0 / 0 results";
            UpdateFindButtons();
            return;
        }

        foreach (var message in Vm.Messages)
        {
            if (string.IsNullOrEmpty(message.Content)) continue;

            var localIndex = 0;
            var cursor = 0;
            while (cursor < message.Content.Length)
            {
                var index = message.Content.IndexOf(_searchQuery, cursor, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;

                _findMatches.Add(new FindMatch(message, localIndex));
                localIndex++;
                cursor = index + _searchQuery.Length;
            }

            if (localIndex > 0)
            {
                message.FindQuery = _searchQuery;
            }
        }

        _currentFindIndex = _findMatches.Count > 0 ? 0 : -1;
        ApplyCurrentFindMatch(scroll: _findMatches.Count > 0);
    }

    private void SelectNextMatch()
    {
        if (_findMatches.Count == 0) return;

        _currentFindIndex = (_currentFindIndex + 1 + _findMatches.Count) % _findMatches.Count;
        ApplyCurrentFindMatch(scroll: true);
    }

    private void SelectPreviousMatch()
    {
        if (_findMatches.Count == 0) return;

        _currentFindIndex = (_currentFindIndex - 1 + _findMatches.Count) % _findMatches.Count;
        ApplyCurrentFindMatch(scroll: true);
    }

    private void ApplyCurrentFindMatch(bool scroll)
    {
        foreach (var message in Vm.Messages)
        {
            message.ActiveFindMatchIndex = -1;
        }

        if (_currentFindIndex >= 0 && _currentFindIndex < _findMatches.Count)
        {
            var match = _findMatches[_currentFindIndex];
            match.Message.FindQuery = _searchQuery;
            match.Message.ActiveFindMatchIndex = match.MatchIndexInMessage;
            if (match.Message.IsContentCollapsed)
            {
                match.Message.IsContentExpanded = true;
            }

            if (scroll)
            {
                MessageList.ScrollIntoView(match.Message);
            }
        }

        FindCounterText.Text = _findMatches.Count == 0
            ? "0 / 0 results"
            : $"{_currentFindIndex + 1} / {_findMatches.Count} results";
        UpdateFindButtons();
    }

    private void ClearFindHighlights()
    {
        foreach (var message in Vm.Messages)
        {
            message.FindQuery = "";
            message.ActiveFindMatchIndex = -1;
        }
    }

    private void UpdateFindButtons()
    {
        var hasMatches = _findMatches.Count > 0;
        PreviousFindButton.IsEnabled = hasMatches;
        NextFindButton.IsEnabled = hasMatches;
    }

    private readonly record struct FindMatch(ChatMessage Message, int MatchIndexInMessage);

    // --- Interactive turns (ask_user / approval_needed / plan_review) ---
    // Rendered as persistent inline chat bubbles (see ChatViewModel.AddAskUserBubble
    // etc. and the DataTemplate cards in ChatPage.xaml) rather than ContentDialogs,
    // so the question/decision stays in the conversation history.

    private async void AskUserSubmit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message)
        {
            await Vm.SubmitAskUserAnswerAsync(message);
        }
    }

    private async void ApprovalOnce_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message)
        {
            await Vm.SubmitApprovalAsync(message, true, "once");
        }
    }

    private async void ApprovalSession_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message)
        {
            await Vm.SubmitApprovalAsync(message, true, "session");
        }
    }

    private async void ApprovalReject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message)
        {
            await Vm.SubmitApprovalAsync(message, false, "once");
        }
    }

    private async void PlanApprove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message)
        {
            await Vm.SubmitPlanReviewAsync(message, true);
        }
    }

    private async void PlanRequestChanges_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message)
        {
            await Vm.SubmitPlanReviewAsync(message, false);
        }
    }
}
