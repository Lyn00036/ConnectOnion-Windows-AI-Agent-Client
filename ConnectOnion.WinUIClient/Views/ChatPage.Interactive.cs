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
/// <see cref="ChatPage"/>: the interactive-turn cards (ask_user / approval_needed / plan_review).
/// These are the handlers that answer an agent which is blocked waiting on the user, so each one
/// resolves a pending decision rather than merely updating the view.
/// </summary>
public sealed partial class ChatPage
{
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
