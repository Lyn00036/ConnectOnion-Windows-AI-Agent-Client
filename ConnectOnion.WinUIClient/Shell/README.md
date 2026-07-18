# Shell/

The `MainWindow` partial class, split by the window-level concern each file owns.

All files here declare `namespace ConnectOnion.WinUIClient` — the **root** namespace, not
`ConnectOnion.WinUIClient.Shell`. That is required, not an oversight: a partial class has one
namespace across every file, and `MainWindow.xaml.cs` must stay in the project root beside
`MainWindow.xaml` for the XAML compiler to pair them. The folder is physical organisation only.

| File | Owns |
|---|---|
| `MainWindow.FileMenu.cs` | File menu commands and their shortcuts |
| `MainWindow.EditMenu.cs` | Undo/cut/copy/paste against the last focused text box |
| `MainWindow.ViewMenu.cs` | Zoom, full screen, sidebar toggle, and the shared find overlay |
| `MainWindow.HelpMenu.cs` | About / keyboard-shortcuts dialogs |
| `MainWindow.Shortcuts.cs` | Pushes live chords into the menu items' accelerator text |
| `MainWindow.Notifications.cs` | In-app toast host wiring |
| `MainWindow.DragDrop.cs` | Window-wide "drop it on the composer" hint |
| `MainWindow.Tray.cs` | Tray icon behaviour: show/hide, context menu, close-to-tray |
| `MainWindow.Tray.Interop.cs` | The Win32 P/Invoke the tray needs (WinUI has no tray API) |
