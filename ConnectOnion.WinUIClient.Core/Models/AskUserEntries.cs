using CommunityToolkit.Mvvm.ComponentModel;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// One dynamic named field in an ask_user form (e.g. "username"), bound to an
/// inline TextBox in the chat bubble. Plain `set` (not `init`) on every property:
/// the XAML compiler's generated x:Bind reflection metadata needs settable
/// properties for any type reachable from a DataTemplate (see the identical note
/// on PendingAttachment/ChatAttachment).
/// </summary>
public sealed partial class AskUserFieldEntry : Common.ObservableObject
{
    public AskUserFieldEntry() => Value = "";

    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Placeholder { get; set; }

    [ObservableProperty]
    public partial string Value { get; set; }
}

/// <summary>
/// One selectable option in an ask_user turn, in either mode. The card is drawn by hand rather
/// than by a CheckBox/RadioButton, so exclusivity is this type's job instead of the framework's:
/// <see cref="Toggle"/> asks <see cref="Owner"/> to apply whichever rule the turn is under.
/// </summary>
public sealed partial class AskUserOptionEntry : Common.ObservableObject
{
    public string Text { get; set; } = "";

    /// <summary>
    /// The card this option belongs to, so a click can reach its siblings to deselect them.
    /// Plain `set` and untyped-by-interface on purpose: every type reachable from a DataTemplate
    /// needs settable properties for the XAML compiler's generated x:Bind metadata (see the note
    /// on <see cref="AskUserFieldEntry"/>).
    /// </summary>
    public ChatMessage? Owner { get; set; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>Handles a click on the option's card. Single-select turns clear the siblings and
    /// keep the clicked one on (clicking the selected option again is a no-op, matching a radio
    /// group); multi-select turns just flip it.</summary>
    public void Toggle()
    {
        if (Owner is { AskUserMultiSelect: false })
        {
            foreach (var sibling in Owner.AskUserOptionEntries)
            {
                if (!ReferenceEquals(sibling, this)) sibling.IsChecked = false;
            }
            IsChecked = true;
            return;
        }
        IsChecked = !IsChecked;
    }
}
