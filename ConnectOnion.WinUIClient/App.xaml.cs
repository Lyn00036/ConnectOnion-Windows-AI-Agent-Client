using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Services.Notifications;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ConnectOnion.WinUIClient
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// The single top-level window. Created exactly once in <see cref="OnLaunched"/>;
        /// every subsequent activation (a redirected second launch, a taskbar/shell
        /// re-activation) reuses this instance via <see cref="ActivateMainWindow"/>.
        /// Used by code that needs an HWND (e.g. <c>FileOpenPicker</c>) but isn't itself
        /// a <see cref="Window"/> — most notably controls like <c>ChatComposer</c>,
        /// which is hosted inside a <see cref="Microsoft.UI.Xaml.Controls.Page"/> and
        /// has no HWND of its own.
        /// </summary>
        public static Window? MainWindow { get; private set; }

        /// <summary>Set when a redirected second launch arrived before the main window
        /// existed (cold start). Flushed once the window is created so the activation is
        /// honored instead of dropped. Interlocked so it is safe from the non-UI thread
        /// the <c>AppInstance.Activated</c> event fires on.</summary>
        private static int _pendingActivation;

        /// <summary>
        /// Wakes the existing main window — restore from minimized/tray and bring to the
        /// foreground — without ever creating one. Safe to call from any thread (a
        /// redirected second launch arrives on a non-UI thread); the actual window work
        /// is marshalled to the UI thread. If the window does not exist yet (a redirect
        /// during cold start), the request is buffered and replayed by
        /// <see cref="FlushPendingActivation"/> once the window is created.
        /// </summary>
        public static void ActivateMainWindow()
        {
            if (MainWindow is not MainWindow main)
            {
                System.Threading.Interlocked.Exchange(ref _pendingActivation, 1);
                return;
            }
            _ = main.DispatcherQueue.TryEnqueue(main.BringToForeground);
        }

        /// <summary>Replays a buffered cold-start activation, if any. Called from
        /// <see cref="OnLaunched"/> once the single main window exists.</summary>
        internal static void FlushPendingActivation()
        {
            if (System.Threading.Interlocked.Exchange(ref _pendingActivation, 0) != 1) return;
            if (MainWindow is MainWindow main)
                _ = main.DispatcherQueue.TryEnqueue(main.BringToForeground);
        }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            AppStorage.EnsureDirectories();
            AppServices.InitializeNotifications();
            _window = new MainWindow();
            MainWindow = _window;
            _window.Activate();
            HandleColdStartActivation();
            // Honor any second launch that redirected to us before the window existed.
            FlushPendingActivation();
        }

        /// <summary>
        /// If the process was cold-started by clicking a toast, hand its arguments to
        /// the activation router. The router buffers them until the main window signals
        /// it's ready (see <c>MainWindow</c>'s notification wiring), so navigation never
        /// runs before the UI exists.
        /// </summary>
        private static void HandleColdStartActivation()
        {
            try
            {
                var activation = Microsoft.Windows.AppLifecycle.AppInstance
                    .GetCurrent().GetActivatedEventArgs();
                if (activation?.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.AppNotification
                    && activation.Data is Microsoft.Windows.AppNotifications.AppNotificationActivatedEventArgs invoked)
                {
                    AppServices.NotificationActivation.HandleColdStart(invoked.Arguments);
                }
            }
            catch (System.Exception ex)
            {
                NotificationLog.Warn("cold-start activation check failed", ex);
            }
        }
    }
}
