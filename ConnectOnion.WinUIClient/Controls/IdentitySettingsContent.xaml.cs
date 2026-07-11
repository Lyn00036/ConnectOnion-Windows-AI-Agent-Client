using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class IdentitySettingsContent : UserControl
{
    private string _address = "";
    private string _publicKey = "";

    public IdentitySettingsContent()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var identity = IdentityStore.EnsureIdentity();
        _address = identity.Address;
        _publicKey = Hex.ToLowerString(identity.PublicKey);
        AddressText.Text = _address;
        PublicKeyText.Text = _publicKey;
        StorageText.Text = AppDatabase.DatabasePath;
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var value = (sender as FrameworkElement)?.Tag switch
        {
            "address" => _address,
            "publicKey" => _publicKey,
            _ => "",
        };
        if (string.IsNullOrWhiteSpace(value)) return;
        ClipboardService.CopyText(value);
        if (sender is Button button)
        {
            var original = button.Content;
            button.Content = "Copied";
            await System.Threading.Tasks.Task.Delay(900);
            button.Content = original;
        }
    }
}
