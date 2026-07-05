using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class IdentityPage : Page
{
    private string _address = "";
    private string _publicKey = "";

    public IdentityPage()
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

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
            return;
        }

        Frame.Navigate(typeof(SettingsPage));
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

        var data = new DataPackage();
        data.SetText(value);
        Clipboard.SetContent(data);

        if (sender is Button button)
        {
            var original = button.Content;
            button.Content = "Copied";
            await System.Threading.Tasks.Task.Delay(900);
            button.Content = original;
        }
    }
}
