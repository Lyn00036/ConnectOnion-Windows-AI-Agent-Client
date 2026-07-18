using System;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// The add-agent form: one text field that accepts either a relay address or a direct URL,
/// a Test button, and a submit that auto-tests first.
///
/// Single unified input by design — the user pastes whatever their agent gave them and
/// <see cref="ParseInput"/> decides which kind it is, instead of making them pick the right
/// box first. Hosted inline (HomePage, the sidebar) rather than as a dialog, which is why it
/// exposes <see cref="Show"/>/<see cref="Reset"/> and raises <see cref="CloseRequested"/>
/// instead of owning its own window.
///
/// Like the rest of the older code-behind here, this drives its controls imperatively rather
/// than through a view model.
/// </summary>
public sealed partial class AddAgentForm : UserControl
{
    // Title and submit label are DPs because the hosts word them differently — HomePage
    // switches between "Add agent" and "Add another agent" depending on whether this is the
    // user's first one (see HomePage.UpdateAddAgentLabels).
    public static readonly DependencyProperty FormTitleProperty =
        DependencyProperty.Register(nameof(FormTitle), typeof(string), typeof(AddAgentForm), new PropertyMetadata("Add agent"));

    public static readonly DependencyProperty SubmitLabelProperty =
        DependencyProperty.Register(nameof(SubmitLabel), typeof(string), typeof(AddAgentForm), new PropertyMetadata("Add"));

    private bool _isBusy;

    /// <summary>Whether the *current* input text has passed a connection test. Cleared on
    /// every keystroke (see <see cref="InputBox_TextChanged"/>), so it can never certify an
    /// endpoint the user has since edited.</summary>
    private bool _connectionTested;

    /// <summary>The agent's self-reported name from <c>/info</c>, captured during the test so
    /// submit can use it instead of a generated fallback. Cleared alongside
    /// <see cref="_connectionTested"/> for the same reason.</summary>
    private string? _detectedName;

    public event Action<AgentConfig>? AgentAdded;
    public event Action? CloseRequested;

    public AddAgentForm() => InitializeComponent();

    public string FormTitle
    {
        get => (string)GetValue(FormTitleProperty);
        set => SetValue(FormTitleProperty, value);
    }

    public string SubmitLabel
    {
        get => (string)GetValue(SubmitLabelProperty);
        set => SetValue(SubmitLabelProperty, value);
    }

    public void FocusFirstField() => InputBox.Focus(FocusState.Programmatic);

    public void Show()
    {
        Visibility = Visibility.Visible;
        FocusFirstField();
    }

    /// <summary>Returns the form to first-open state and hides it. Called both on cancel and
    /// after a successful add — the control is reused by its host rather than recreated, so
    /// every field including the busy/test flags has to be cleared explicitly or the next
    /// open would inherit the last attempt's state.</summary>
    public void Reset()
    {
        InputBox.Text = "";
        _detectedName = null;
        _connectionTested = false;
        _isBusy = false;
        SubmitButton.IsEnabled = false;
        TestButton.IsEnabled = true;
        BusyRing.IsActive = false;
        BusyRing.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Reset();
        CloseRequested?.Invoke();
    }

    /// <summary>Any edit invalidates the previous test result. This is the safety property
    /// that lets submit trust <see cref="_connectionTested"/>: the flag can only ever refer
    /// to the text currently in the box, so it is impossible to test one endpoint, edit the
    /// field, and add the untested one.</summary>
    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _detectedName = null;
        _connectionTested = false;
        SubmitButton.IsEnabled = false;
        StatusText.Visibility = Visibility.Collapsed;
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Enter submits from the field, so the common case (paste, press Enter) never needs
        // the mouse. Marked Handled to stop the key bubbling to the host page. Fire-and-forget
        // is safe because AddAgentAsync re-tests and reports failures through ShowStatus; the
        // _isBusy check is what stops Enter-mashing from starting overlapping attempts.
        if (e.Key == Windows.System.VirtualKey.Enter && !_isBusy)
        {
            e.Handled = true;
            _ = AddAgentAsync();
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
        => await TestConnectionAsync();

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        await AddAgentAsync();
    }

    /// <summary>
    /// Validates the input, rejects duplicates, then actually probes the agent. Returns
    /// whether the agent answered — submit uses that to decide whether to proceed.
    ///
    /// Order matters: the cheap local checks (shape, then duplicate) run before the network
    /// call, so a typo or an agent the user already has costs no round-trip and reports
    /// immediately.
    /// </summary>
    private async Task<bool> TestConnectionAsync()
    {
        var input = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            ShowStatus("Enter an address or Direct URL.", isError: true);
            return false;
        }

        var (address, directUrl) = ParseInput(input);

        if (!string.IsNullOrEmpty(address) && !AgentAddressValidator.IsValid(address))
        {
            ShowStatus(AgentAddressValidator.ValidationMessage, isError: true);
            return false;
        }

        if (!string.IsNullOrEmpty(directUrl) && !Uri.TryCreate(directUrl, UriKind.Absolute, out _))
        {
            ShowStatus("Enter a valid URL (e.g. wss://agent.example.com/ws)", isError: true);
            return false;
        }

        var agentsState = await AppServices.Agents.LoadAsync();
        if (ShowDuplicateStatus(AgentEndpointDuplicateDetector.Find(agentsState.Agents, address, directUrl)))
            return false;

        SetBusy(true);
        try
        {
            var result = await ConnectionTester.TestAsync(
                string.IsNullOrWhiteSpace(address) ? null : address,
                string.IsNullOrWhiteSpace(directUrl) ? null : directUrl);

            _connectionTested = result.Ok;
            _detectedName = result.Ok ? result.AgentName : null;
            ShowStatus(result.Detail, !result.Ok);

            if (result.Ok)
                SubmitButton.IsEnabled = true;

            return result.Ok;
        }
        finally
        {
            // finally, not after the return: a probe that throws must still re-enable the
            // form, or a transient network error leaves the user with a permanently busy
            // dialog and no way back except closing it.
            SetBusy(false);
        }
    }

    /// <summary>
    /// Adds the agent, testing first if the user went straight to submit (or pressed Enter).
    ///
    /// The validation here duplicates <see cref="TestConnectionAsync"/>'s on purpose: this is
    /// reachable via Enter without the test path having run, so it cannot assume anything has
    /// been checked. The duplicate scan in particular is re-run because the agent list can
    /// have changed between the test and the submit.
    /// </summary>
    private async Task AddAgentAsync()
    {
        var input = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            ShowStatus("Enter an address or Direct URL.", isError: true);
            return;
        }

        var (address, directUrl) = ParseInput(input);

        if (!string.IsNullOrEmpty(address) && !AgentAddressValidator.IsValid(address))
        {
            ShowStatus(AgentAddressValidator.ValidationMessage, isError: true);
            return;
        }

        var agentsState = await AppServices.Agents.LoadAsync();
        if (ShowDuplicateStatus(AgentEndpointDuplicateDetector.Find(agentsState.Agents, address, directUrl)))
            return;

        // Auto-test first, then submit: reaching an unreachable agent is the single most
        // likely mistake here, and catching it now gives a real error instead of an agent
        // that sits permanently offline in the sidebar. A failed test aborts the add.
        if (!_connectionTested)
        {
            var ok = await TestConnectionAsync();
            if (!ok) return;
        }

        var agent = new AgentConfig
        {
            Id = Guid.NewGuid().ToString(),
            // The agent's own /info name if the probe got one; otherwise a stable stand-in
            // derived from the endpoint, so the row is at least identifiable.
            Name = _detectedName ?? CreateFallbackName(input),
            Address = address,
            // Normalized to null rather than "": the duplicate detector and the connection
            // code both treat null as "not configured", and an empty string is not that.
            DirectUrl = string.IsNullOrWhiteSpace(directUrl) ? null : directUrl,
        };

        agentsState.Agents.Add(agent);
        // Select it immediately — the user just added this agent, so it is what they want to
        // be looking at when the host navigates on AgentAdded.
        agentsState.SelectedAgentId = agent.Id;
        await AppServices.Agents.SaveAsync(agentsState);

        // Reset before raising: the handler navigates away, and the form has to be clean for
        // its next open rather than still showing this agent's details.
        Reset();
        AgentAdded?.Invoke(agent);
    }

    /// <summary>Auto-detect input type: 0x prefix → relay address, otherwise Direct URL.
    /// Only the prefix is examined — full address validation is
    /// <see cref="AgentAddressValidator"/>'s job, so that a malformed <c>0x…</c> string is
    /// reported as a bad address rather than silently treated as a URL.</summary>
    private static (string address, string directUrl) ParseInput(string input)
    {
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return (input, "");
        return ("", input);
    }

    /// <summary>Single place that owns the form's enabled/spinner state, so no path can
    /// re-enable half of it.</summary>
    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        TestButton.IsEnabled = !isBusy;
        // Note the extra condition: leaving busy does *not* enable submit on its own, only a
        // passed test does. A failed probe must not leave the button clickable.
        SubmitButton.IsEnabled = !isBusy && _connectionTested;
        BusyRing.IsActive = isBusy;
        BusyRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        InputBox.IsEnabled = !isBusy;
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            isError ? "DangerBrush" : "SuccessBrush"];
        StatusText.Visibility = Visibility.Visible;
    }

    /// <summary>Reports a duplicate endpoint if there is one. Returns true when the caller
    /// should abort — reading as <c>if (ShowDuplicateStatus(...)) return;</c> at both call
    /// sites.</summary>
    private bool ShowDuplicateStatus(AgentEndpointDuplicate duplicate)
    {
        if (duplicate == AgentEndpointDuplicate.None) return false;

        // AgentEndpointDuplicate is a [Flags] enum, so the combined case has to be matched
        // first — a plain `Address` arm would also match Address|DirectUrl in a switch that
        // tested it earlier, and the user would be told only half of what collided.
        var msg = duplicate switch
        {
            AgentEndpointDuplicate.Address | AgentEndpointDuplicate.DirectUrl => "An agent with this address and Direct URL already exists.",
            AgentEndpointDuplicate.Address => "An agent with this address already exists.",
            _ => "An agent with this Direct URL already exists.",
        };
        ShowStatus(msg, isError: true);
        return true;
    }

    private static string CreateFallbackName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "Agent";
        return $"Agent {input[..Math.Min(8, input.Length)]}";
    }
}
