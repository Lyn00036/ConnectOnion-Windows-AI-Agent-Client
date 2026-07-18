using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// Settings → Identity: shows this installation's agent address, public key, and database
/// path, each with a copy button. Read-only by design — the address is derived from the
/// Ed25519 seed in <c>identity_keys</c>, so there is nothing here a user could edit, and
/// regenerating it would invalidate every authorization the agents have granted.
/// </summary>
public sealed partial class IdentitySettingsContent : UserControl
{
    // Kept as fields rather than re-read from the TextBlocks on copy: the displayed text may
    // be trimmed or styled, and the clipboard must get the exact value.
    private string _address = "";
    private string _publicKey = "";

    public IdentitySettingsContent()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // EnsureIdentity is cheap after first run (it reads the already-decrypted seed) and
        // never generates here in practice — startup has long since created the identity.
        var identity = IdentityStore.EnsureIdentity();
        _address = identity.Address;
        _publicKey = Hex.ToLowerString(identity.PublicKey);
        AddressText.Text = _address;
        PublicKeyText.Text = _publicKey;
        StorageText.Text = AppDatabase.DatabasePath;
    }

    /// <summary>Copies whichever value the clicked button is tagged with, and flashes
    /// "Copied" on it for ~1s as the only confirmation (there is no toast for this).</summary>
    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        // The Tag is the discriminator, so both buttons share this one handler; an unknown
        // tag copies nothing rather than copying the wrong secret-adjacent value.
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
            // Restoring the caption means capturing it first — the label differs per button
            // and is localized, so it cannot be re-derived from a literal here.
            var original = button.Content;
            button.Content = "Copied";
            await System.Threading.Tasks.Task.Delay(900);
            // KNOWN HAZARD: this resumes on the dispatcher, which keeps pumping after
            // Window.Closed. Closing the window inside this 900ms window resumes against a
            // tree the framework is tearing down — the access-violation-in-Microsoft.UI.Xaml
            // failure mode described in CLAUDE.md. Every other async-void-after-await site in
            // the app (see InAppNotificationHost.Remove) carries an explicit "did we shut
            // down?" check on the far side of the await; this one does not yet.
            button.Content = original;
        }
    }
}
