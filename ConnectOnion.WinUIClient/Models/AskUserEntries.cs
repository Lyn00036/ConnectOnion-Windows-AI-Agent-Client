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

/// <summary>One selectable option in an ask_user multi-select list, bound to an inline CheckBox.</summary>
public sealed partial class AskUserOptionEntry : Common.ObservableObject
{
    public string Text { get; set; } = "";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
