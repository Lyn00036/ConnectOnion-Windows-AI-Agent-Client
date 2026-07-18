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

/// <summary>
/// <see cref="ChatPage"/>: scroll position, the stick-to-bottom behaviour, the reading-column
/// inset, and the load overlay that hides list virtualization while a conversation is realizing.
/// <para>This is the most timing-sensitive code on the page: it waits on container realization
/// and frame ticks rather than assuming layout has settled, which is why it is worth keeping
/// apart from the conversation lifecycle.</para>
/// </summary>
public sealed partial class ChatPage
{
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
}
