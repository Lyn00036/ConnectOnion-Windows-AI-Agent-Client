using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class AgentDetailPage : Page
{
    public AgentDetailViewModel Vm { get; } = new();

    public AgentDetailPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Vm.LoadAsync();
        Unloaded += (_, _) => Vm.Cleanup();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await Vm.RefreshAsync();
}
