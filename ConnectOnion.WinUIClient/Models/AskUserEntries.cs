using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// One dynamic named field in an ask_user form (e.g. "username"), bound to an
/// inline TextBox in the chat bubble. Plain `set` (not `init`) on every property:
/// the XAML compiler's generated x:Bind reflection metadata needs settable
/// properties for any type reachable from a DataTemplate (see the identical note
/// on PendingAttachment/ChatAttachment).
/// </summary>
public sealed class AskUserFieldEntry : ObservableObject
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Placeholder { get; set; }

    private string _value = "";
    public string Value { get => _value; set => SetProperty(ref _value, value); }
}

/// <summary>One selectable option in an ask_user multi-select list, bound to an inline CheckBox.</summary>
public sealed class AskUserOptionEntry : ObservableObject
{
    public string Text { get; set; } = "";

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
}
