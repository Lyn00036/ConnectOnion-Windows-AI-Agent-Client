using System;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class AddAgentForm : UserControl
{
    public static readonly DependencyProperty FormTitleProperty =
        DependencyProperty.Register(nameof(FormTitle), typeof(string), typeof(AddAgentForm), new PropertyMetadata("Add agent"));

    public static readonly DependencyProperty SubmitLabelProperty =
        DependencyProperty.Register(nameof(SubmitLabel), typeof(string), typeof(AddAgentForm), new PropertyMetadata("Add"));

    private bool _isBusy;
    private bool _connectionTested;
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

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _detectedName = null;
        _connectionTested = false;
        SubmitButton.IsEnabled = false;
        StatusText.Visibility = Visibility.Collapsed;
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
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
            SetBusy(false);
        }
    }

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

        if (!_connectionTested)
        {
            // Auto-test first, then submit
            var ok = await TestConnectionAsync();
            if (!ok) return;
        }

        var agent = new AgentConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = _detectedName ?? CreateFallbackName(input),
            Address = address,
            DirectUrl = string.IsNullOrWhiteSpace(directUrl) ? null : directUrl,
        };

        agentsState.Agents.Add(agent);
        agentsState.SelectedAgentId = agent.Id;
        await AppServices.Agents.SaveAsync(agentsState);

        Reset();
        AgentAdded?.Invoke(agent);
    }

    /// <summary>Auto-detect input type: 0x prefix → relay address, otherwise Direct URL.</summary>
    private static (string address, string directUrl) ParseInput(string input)
    {
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return (input, "");
        return ("", input);
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        TestButton.IsEnabled = !isBusy;
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

    private bool ShowDuplicateStatus(AgentEndpointDuplicate duplicate)
    {
        if (duplicate == AgentEndpointDuplicate.None) return false;

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
