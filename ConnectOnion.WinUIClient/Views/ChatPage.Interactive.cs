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

    /// <summary>
    /// Answers with the clicked option. On a single-select turn the click <i>is</i> the answer —
    /// picking one and then confirming it is the same decision twice, and the card offers no other
    /// way to change your mind afterwards anyway.
    ///
    /// A multi-select turn cannot work that way: the first click would send before the user had
    /// chosen the rest, so there a click only toggles and the answer still goes on the composer's
    /// send button. The exclusivity rule itself lives on the entry, so it holds however the card
    /// is rendered.
    /// </summary>
    private async void AskUserOption_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AskUserOptionEntry entry) return;

        entry.Toggle();
        if (entry.Owner is { AskUserMultiSelect: false } message)
        {
            await Vm.SubmitAskUserAnswerAsync(message);
        }
    }

    private async void AskUserSkip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message)
        {
            await Vm.SkipAskUserAsync(message);
        }
    }

    // The approval menu's two affirmative rows rest muted and come up to full strength under the
    // pointer or keyboard focus. Focus is wired as well as hover so a keyboard user sees the same
    // row highlighted that a mouse user would — otherwise tabbing through a menu of dimmed rows
    // gives no sign of where you are. The resting value lives here rather than being re-read from
    // the Style, so the two states are stated in one place.
    private const double ApprovalRowRestOpacity = 0.62;

    private static void SetApprovalRowEmphasis(object sender, bool emphasized)
    {
        if (sender is Button row) row.Opacity = emphasized ? 1d : ApprovalRowRestOpacity;
    }

    private void ApprovalMenuItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        => SetApprovalRowEmphasis(sender, true);

    private void ApprovalMenuItem_PointerExited(object sender, PointerRoutedEventArgs e)
        => SetApprovalRowEmphasis(sender, false);

    private void ApprovalMenuItem_GotFocus(object sender, RoutedEventArgs e)
        => SetApprovalRowEmphasis(sender, true);

    private void ApprovalMenuItem_LostFocus(object sender, RoutedEventArgs e)
        => SetApprovalRowEmphasis(sender, false);

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

    /// <summary>Unfolds the approval card's request details. Deliberately local-only: it answers
    /// nothing and leaves the approval pending, so inspecting a request never costs the user their
    /// decision.</summary>
    private void ApprovalExplain_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage message)
        {
            message.ToggleApprovalExplain();
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
