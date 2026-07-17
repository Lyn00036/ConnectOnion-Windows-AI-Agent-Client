using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Presentation;

/// <summary>
/// Picks the one template a <see cref="ChatMessage"/> actually needs, instead of building every
/// bubble kind into every row and hiding all but one.
///
/// This is safe to resolve once per container because a message's kind is fixed by the time it
/// reaches the list: <c>Role</c>, <c>EventKind</c> and <c>ToolActivity</c> are all assigned in the
/// object initializer (see <c>ChatTurnProjection</c>) or while loading history
/// (<c>ConversationRepository.RowToMessage</c>, <c>ToolActivityMigration</c>) — never after the
/// message has been added. Everything that <i>does</i> mutate live (Content, Status, EventMeta,
/// attachments) only changes what a bubble shows, not which bubble it is, and stays on ordinary
/// bindings inside the chosen template. If a kind ever becomes mutable post-add, this selector
/// will not re-run for it and the row would keep its original template.
/// </summary>
public sealed partial class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? User { get; set; }
    public DataTemplate? Agent { get; set; }
    public DataTemplate? Usage { get; set; }
    public DataTemplate? Activity { get; set; }
    public DataTemplate? ToolActivity { get; set; }
    public DataTemplate? AskUser { get; set; }
    public DataTemplate? Approval { get; set; }
    public DataTemplate? PlanReview { get; set; }

    /// <summary>Renders nothing. Covers event kinds this build has no card for — a legacy
    /// <c>"tool"</c> row that migration left behind, or a <c>tool_activity</c> row whose payload
    /// failed to deserialize — which the previous all-in-one template also drew as a blank row.</summary>
    public DataTemplate? Empty { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        if (item is not ChatMessage message) return Empty;

        if (message.IsUser) return User;
        if (message.IsAgent) return Agent;
        if (message.IsTurnUsageEvent) return Usage;
        if (message.IsActivityEvent) return Activity;
        if (message.IsToolActivityEvent) return ToolActivity;
        if (message.IsAskUserEvent) return AskUser;
        if (message.IsApprovalEvent) return Approval;
        if (message.IsPlanReviewEvent) return PlanReview;

        return Empty;
    }
}
