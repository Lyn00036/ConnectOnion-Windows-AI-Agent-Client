using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient;
using ConnectOnion.WinUIClient.Controls;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.ViewModels;
using FluentIcons.Common;
using FluentIcons.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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

public sealed partial class ChatPage : Page, IFindHost, IReloadablePage
{
    /// <summary>Width of the centred conversation column; see the ChatColumnWidth resource in App.xaml.</summary>
    private static double ChatColumnMaxWidth => (double)Application.Current.Resources["ChatColumnWidth"];

    /// <summary>Persisted scroll positions keyed by session id, survives page recreation.</summary>
    private static readonly ConcurrentDictionary<string, double> ScrollPositions = new();

    public ChatViewModel Vm { get; } = App.GetService<ChatViewModel>();
    private ScrollViewer? _messageScrollViewer;
    private bool _isLoadingConversation;
    private bool _isAutoScrollQueued;
    private bool _stickToBottom = true;
    private ComposerSubmission? _pendingInitialSubmission;
    private MainWindow? _hostWindow;
    private CancellationTokenSource? _loadCts;

    public ChatPage()
    {
        InitializeComponent();

        // Reuse this one instance across navigations instead of rebuilding the whole page —
        // XAML tree, ChatComposer's Win2D canvas, view model, bindings — every time the user
        // crosses page types (e.g. AgentDetailPage → ChatPage). Without this the same-type
        // reuse in MainWindow.NavigateTo only covers chat→chat, so an agent→chat switch built a
        // fresh tree each click and the discarded ones piled up. The Load/Unload wiring below
        // is symmetric so a reused instance behaves like a fresh one.
        NavigationCacheMode = NavigationCacheMode.Required;

        Vm.Messages.CollectionChanged += Messages_CollectionChanged;
        Vm.PropertyChanged += Vm_PropertyChanged;
        _findDebounceTimer.Tick += FindDebounceTimer_Tick;
        ChatRoot.KeyDown += ChatRoot_KeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        // No longer viewing this conversation — clear it so notifications resume.
        ReportViewing(active: false);

        // Persist scroll position so returning to this conversation restores it.
        SaveScrollPosition();

        _findDebounceTimer.Stop();

        // The page is cached and reused (NavigationCacheMode.Required above), so only the
        // per-view work is torn down here: drop the live run subscription so a backgrounded page
        // stops projecting into a list nobody is looking at — LoadAsync re-subscribes on return.
        // The presence hook and Vm.PropertyChanged handler are left attached on purpose: this is
        // the single, reused instance (the constructor never runs again), so one permanent hook
        // is correct, not a leak. Detaching them here would leave them dead after the first
        // round-trip.
        try { Vm.DetachRun(); }
        catch { /* Best-effort cleanup. */ }
    }

    /// <summary>Reports to <c>WindowPresenceService</c> whether this window is
    /// currently showing this conversation, so the notification policy can suppress
    /// notifications the user would already see. The host window is captured while
    /// loaded so it can still be cleared during unload (when XamlRoot may be gone).</summary>
    private void ReportViewing(bool active)
    {
        try
        {
            if (active)
            {
                _hostWindow = MainWindow.FromXamlRoot(XamlRoot);
                if (_hostWindow is not null)
                    AppServices.WindowPresence.SetViewing(_hostWindow, Vm.AgentId, Vm.SessionId);
            }
            else
            {
                if (_hostWindow is not null)
                    AppServices.WindowPresence.SetViewing(_hostWindow, null, null);
                _hostWindow = null;
            }
        }
        catch { /* Best-effort presence reporting. */ }
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

        await LoadConversationAsync();
    }

    /// <summary>
    /// Re-points this page at whichever conversation is now active, without rebuilding the page
    /// (see <see cref="IFindHost"/>'s neighbour, <see cref="IReloadablePage"/>, for why).
    /// </summary>
    public async System.Threading.Tasks.Task ReloadAsync()
    {
        SaveScrollPosition();
        _pendingInitialSubmission = null;
        _stickToBottom = true;
        ScrollToLatestButton.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Visible;
        await LoadConversationAsync();
    }

    /// <summary>Remembers where the *outgoing* conversation was scrolled to, so coming back to
    /// it lands in the same place. Keyed by session id, consumed (and removed) on restore.</summary>
    private void SaveScrollPosition()
    {
        try
        {
            if (Vm.SessionId is { } sid && _messageScrollViewer is not null)
                ScrollPositions[sid] = _messageScrollViewer.VerticalOffset;
        }
        catch { /* Best-effort scroll persistence. */ }
    }

    private async System.Threading.Tasks.Task LoadConversationAsync()
    {
        _loadCts?.Cancel();
        var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;
        _isLoadingConversation = true;
        try
        {
            await Vm.LoadAsync(loadCts.Token);
        }
        catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
        {
            return;
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
            if (ReferenceEquals(_loadCts, loadCts)) _loadCts = null;
            loadCts.Dispose();
        }

        // Rapid switching can navigate away — and unload this cached page — during the DB load
        // above. Touching the composer/overlay dependency properties or the message list while
        // this page is detached from the visual tree throws E_UNEXPECTED (0x8000FFFF). Bail if we
        // are no longer loaded; the next OnLoaded re-runs against the current selection.
        if (!IsLoaded) return;

        // Tell the notification layer this window is now showing this conversation,
        // so a reply that lands here is suppressed (the user already sees it).
        ReportViewing(active: true);

        // Show empty state when there are no messages after loading completes.
        if (Vm.Messages.Count == 0)
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
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

    private async void Composer_StopRequested(object? sender, System.EventArgs e)
        => await Vm.StopAsync();

    private async void Composer_ModeChangeRequested(object? sender, string mode)
        => await Vm.SetModeAsync(mode);

    private async void OfflineNotice_RecheckRequested(object? sender, System.EventArgs e)
        => await Vm.RecheckPresenceAsync();

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

        ClipboardService.CopyText(message.Content);

        if (sender is Button { Content: FluentIcon fluentIcon } iconButton)
        {
            var originalIcon = fluentIcon.Icon;
            iconButton.IsHitTestVisible = false;
            fluentIcon.Icon = Icon.Checkmark;
            AutomationProperties.SetName(iconButton, "Copied");
            await System.Threading.Tasks.Task.Delay(1000);
            fluentIcon.Icon = originalIcon;
            AutomationProperties.SetName(iconButton, message.IsUser ? "Copy message" : "Copy response");
            iconButton.IsHitTestVisible = true;
            return;
        }

        if (sender is Button { Content: IconText iconText })
        {
            var original = iconText.Text;
            iconText.Text = "Copied";
            await System.Threading.Tasks.Task.Delay(1000);
            iconText.Text = original;
        }
    }

    private static ChatMessage? MessageFrom(object sender)
        => (sender as FrameworkElement)?.DataContext as ChatMessage;

    private void MessageActions_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (MessageFrom(sender) is { IsEditing: false } message)
        {
            message.AreUserActionsVisible = true;
        }
    }

    private void MessageActions_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (MessageFrom(sender) is { IsEditing: false } message)
        {
            message.AreUserActionsVisible = false;
        }
    }

    private void MessageActions_GotFocus(object sender, RoutedEventArgs e)
    {
        if (MessageFrom(sender) is { IsEditing: false } message)
        {
            message.AreUserActionsVisible = true;
        }
    }

    private void MessageActions_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement root || MessageFrom(sender) is not { } message) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            var focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            if (!message.IsEditing && (focused is null || !IsDescendantOf(focused, root)))
            {
                message.AreUserActionsVisible = false;
            }
        });
    }

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        for (DependencyObject? current = child; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }
        return false;
    }

    private void EditUserMessage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message && !Vm.IsProcessing)
        {
            message.BeginEditing();
        }
    }

    private void CancelEditUserMessage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message)
        {
            message.CancelEditing();
            message.AreUserActionsVisible = true;
        }
    }

    private async void SaveEditUserMessage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ChatMessage message) return;

        var replacement = message.EditDraft.Trim();
        if (replacement.Length == 0) return;

        message.CancelEditing();
        if (await Vm.BranchFromMessageAsync(message, replacement))
        {
            QueueScrollToEnd(force: true);
            return;
        }

        message.BeginEditing();
        message.EditDraft = replacement;
    }

    private async void RetryUserMessage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message &&
            await Vm.BranchFromMessageAsync(message, message.Content))
        {
            QueueScrollToEnd(force: true);
        }
    }

    private async void RetryAgentMessage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message &&
            await Vm.RetryFromAgentMessageAsync(message))
        {
            QueueScrollToEnd(force: true);
        }
    }

    private async void ActivityHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button header && header.Tag is ChatMessage message)
        {
            await DisclosureAnimation.ToggleAsync(
                header,
                DisclosureAnimation.FindContentAfter(header),
                message.IsActivityExpanded,
                message.ToggleActivityExpanded);
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

    private void ChatRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Centre a fixed-width reading column instead of letting the thread span the
        // whole window. Left-aligned agent turns and right-aligned user turns are
        // ~1200px apart on a maximised window otherwise, which stops them reading as
        // one conversation. The inset never drops below the edge margin, so a narrow
        // window degrades to the old flush-to-edge behaviour rather than going negative.
        var edgeInset = e.NewSize.Width < 640 ? 20 : 52;
        var centringInset = (e.NewSize.Width - ChatColumnMaxWidth) / 2;
        var horizontalInset = Math.Max(edgeInset, centringInset);
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
        QueueFindRefresh();

        // Hide the empty state once a real message appears.
        if (Vm.Messages.Count > 0 && EmptyStatePanel.Visibility == Visibility.Visible)
            EmptyStatePanel.Visibility = Visibility.Collapsed;

        if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace))
        {
            return;
        }

        if (!_stickToBottom)
        {
            ScrollToLatestButton.Visibility = Visibility.Visible;
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

        // A short chat realizes in a fraction of a second; a long, multi-round one
        // needs longer — both to materialize its containers and, after the scroll
        // restore below, to re-virtualize the fresh (and heavy: markdown, tool
        // timelines, images) bubbles the restore exposes near the bottom. Use one
        // budget that scales with history for both waits instead of a flat timeout.
        var budget = OverlayReadyBudget();

        // First wait: enough top-of-list containers to consider the viewport painted
        // (typical viewport holds 8–15 items), bounded by the budget so the overlay
        // never sticks if items are zero-height/collapsed.
        await WaitForContainersRealizedAsync(Math.Min(Vm.Messages.Count, 12), budget);

        // One frame so the RichTextBlock/Markdown content inside each realized
        // container can finish its first internal layout pass.
        await NextFrameAsync();

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

            // Second wait: the restore re-virtualizes a new set of bubbles at the
            // landing point. Hold the overlay until that churn goes quiet (a whole
            // frame with nothing new realized) rather than a single fixed frame,
            // which was enough for a short chat but not a long one restored near its
            // end. Still bounded by the budget so it can never hang.
            await WaitForContainerChurnToSettleAsync(budget);

            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        catch
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// How long the loading overlay may stay up before it is force-dismissed, scaled with
    /// conversation length. Longer histories carry more (and heavier) bubbles to lay out and
    /// a scroll-restore that re-virtualizes the bottom of the list, so they need more time
    /// before the overlay can lift without revealing a half-populated viewport. Clamped so a
    /// short chat stays snappy and a pathological one can never freeze the page.
    /// </summary>
    private TimeSpan OverlayReadyBudget()
        => TimeSpan.FromMilliseconds(Math.Clamp(400 + Vm.Messages.Count * 15, 500, 2500));

    /// <summary>Completes once <paramref name="threshold"/> distinct item containers have been
    /// realized, or the timeout elapses — whichever comes first.</summary>
    private async System.Threading.Tasks.Task WaitForContainersRealizedAsync(int threshold, TimeSpan timeout)
    {
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
        try { await Task.WhenAny(tcs.Task, Task.Delay(timeout)); }
        finally { MessageList.ContainerContentChanging -= handler; }
    }

    /// <summary>Waits until container realization has been idle for one full dispatcher frame
    /// (the viewport has stopped churning), bounded by <paramref name="timeout"/>.</summary>
    private async System.Threading.Tasks.Task WaitForContainerChurnToSettleAsync(TimeSpan timeout)
    {
        var churned = true; // assume churn until a quiet frame proves otherwise
        TypedEventHandler<ListViewBase, ContainerContentChangingEventArgs> handler = (_, _) => churned = true;
        MessageList.ContainerContentChanging += handler;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (churned && sw.Elapsed < timeout)
            {
                churned = false;
                await NextFrameAsync();
            }
        }
        finally { MessageList.ContainerContentChanging -= handler; }
    }

    /// <summary>Yields until the next low-priority dispatcher tick, i.e. after the current
    /// layout/render pass — one "frame".</summary>
    private System.Threading.Tasks.Task NextFrameAsync()
    {
        var tcs = new TaskCompletionSource();
        if (!DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => tcs.TrySetResult()))
        {
            tcs.TrySetResult();
        }
        return tcs.Task;
    }
    private void ScrollToLatest_Click(object sender, RoutedEventArgs e)
    {
        _stickToBottom = true;
        ScrollToLatestButton.Visibility = Visibility.Collapsed;
        ScrollToEnd(animate: true);
    }

    private void ScrollToEnd(bool animate = false)
    {
        if (Vm.Messages.Count == 0) return;

        _stickToBottom = true;
        ScrollToLatestButton.Visibility = Visibility.Collapsed;
        _messageScrollViewer ??= FindDescendant<ScrollViewer>(MessageList);
        if (_messageScrollViewer is not null)
        {
            _messageScrollViewer.ChangeView(null, _messageScrollViewer.ScrollableHeight, null, disableAnimation: !animate);
            return;
        }

        MessageList.ScrollIntoView(Vm.Messages[^1]);
    }

    private void MessageScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_messageScrollViewer is null) return;

        var distanceFromBottom = _messageScrollViewer.ScrollableHeight - _messageScrollViewer.VerticalOffset;
        _stickToBottom = distanceFromBottom < 96;
        ScrollToLatestButton.Visibility = _stickToBottom ? Visibility.Collapsed : Visibility.Visible;
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

    // --- Interactive turns (ask_user / approval_needed / plan_review) ---
    // Rendered as persistent inline chat bubbles (see ChatViewModel.AddAskUserBubble
    // etc. and the DataTemplate cards in ChatPage.xaml) rather than ContentDialogs,
    // so the question/decision stays in the conversation history.

    /// <summary>Folds an interactive card open or shut. Purely presentational — it never touches
    /// the pending response, so collapsing a card that still awaits an answer is safe.</summary>
    private async void InteractiveHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button header && header.Tag is ChatMessage message)
        {
            await DisclosureAnimation.ToggleAsync(
                header,
                DisclosureAnimation.FindContentAfter(header),
                message.IsInteractiveExpanded,
                message.ToggleInteractiveExpanded);
        }
    }

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
            await Vm.SubmitApprovalAsync(message, false, "once", ApprovalRejectModes.Hard);
        }
    }

    private async void ApprovalSkip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message)
        {
            await Vm.SubmitApprovalAsync(message, false, "once", ApprovalRejectModes.Soft);
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
