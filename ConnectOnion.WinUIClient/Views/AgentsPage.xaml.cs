using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class AgentsPage : Page
{
    public AgentsViewModel Vm { get; } = new();

    public AgentsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Vm.LoadAsync();
    }

    private void NewAgent_Click(object sender, RoutedEventArgs e) => Vm.CreateAgent();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await Vm.SaveAsync();
        ShowInfo(InfoBarSeverity.Success, "Saved.");
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is { } agent) await Vm.RemoveAsync(agent);
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is not { } agent) return;

        // Persist any pending edits first so the probe uses what the user sees.
        await Vm.SaveAsync();

        TestRing.IsActive = true;
        TestResult.IsOpen = false;
        try
        {
            var result = await Vm.TestConnectionAsync(agent);
            ShowInfo(result.Ok ? InfoBarSeverity.Success : InfoBarSeverity.Error, result.Detail);
        }
        finally
        {
            TestRing.IsActive = false;
        }
    }

    private void ShowInfo(InfoBarSeverity severity, string message)
    {
        TestResult.Severity = severity;
        TestResult.Message = message;
        TestResult.IsOpen = true;
    }
}
