using System;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class AddAgentForm : UserControl
{
    private bool _connectionTested;
    private string? _detectedName;

    public event Action<AgentConfig>? AgentAdded;
    public event Action? CloseRequested;

    public AddAgentForm()
    {
        InitializeComponent();
    }

    public void FocusFirstField()
        => AddressBox.Focus(FocusState.Programmatic);

    public void Reset()
    {
        AddressBox.Text = "";
        DirectUrlBox.Text = "";
        _detectedName = null;
        SetConnectionTested(false);
        StatusText.Visibility = Visibility.Collapsed;
        TestRing.IsActive = false;
        TestRing.Visibility = Visibility.Collapsed;
        TestButton.IsEnabled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Reset();
        CloseRequested?.Invoke();
    }

    private void AddAgentField_TextChanged(object sender, TextChangedEventArgs e)
    {
        _detectedName = null;
        SetConnectionTested(false);
        StatusText.Visibility = Visibility.Collapsed;
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        await TestConnectionAsync();
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        var address = AddressBox.Text.Trim();
        var directUrl = DirectUrlBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(directUrl))
        {
            ShowStatus("Enter a 0x address or Direct URL.", isError: true);
            AddressBox.Focus(FocusState.Programmatic);
            return;
        }

        if (!_connectionTested)
        {
            ShowStatus("Test connection successfully before adding this agent.", isError: true);
            return;
        }

        var agentsState = await AppServices.Agents.LoadAsync();
        var agent = new AgentConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = _detectedName ?? CreateFallbackName(address, directUrl),
            Address = address,
            DirectUrl = string.IsNullOrWhiteSpace(directUrl) ? null : directUrl,
        };

        agentsState.Agents.Add(agent);
        agentsState.SelectedAgentId = agent.Id;
        await AppServices.Agents.SaveAsync(agentsState);

        Reset();
        AgentAdded?.Invoke(agent);
    }

    private async System.Threading.Tasks.Task<bool> TestConnectionAsync()
    {
        var address = AddressBox.Text.Trim();
        var directUrl = DirectUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(directUrl))
        {
            ShowStatus("Enter a 0x address or Direct URL first.", isError: true);
            return false;
        }

        SetBusy(true);
        try
        {
            var result = await ConnectionTester.TestAsync(address, directUrl);
            SetConnectionTested(result.Ok);
            _detectedName = result.Ok ? result.AgentName : null;
            ShowStatus(result.Detail, !result.Ok);
            return result.Ok;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        TestButton.IsEnabled = !isBusy;
        SubmitButton.IsEnabled = !isBusy && _connectionTested;
        TestRing.IsActive = isBusy;
        TestRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetConnectionTested(bool isTested)
    {
        _connectionTested = isTested;
        SubmitButton.IsEnabled = isTested;
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            isError ? "StatusErrorTextBrush" : "StatusSuccessTextBrush"];
        StatusText.Visibility = Visibility.Visible;
    }

    private static string CreateFallbackName(string address, string directUrl)
    {
        var value = string.IsNullOrWhiteSpace(address) ? directUrl : address;
        if (string.IsNullOrWhiteSpace(value)) return "Agent";
        return $"Agent {value[..Math.Min(8, value.Length)]}";
    }
}
