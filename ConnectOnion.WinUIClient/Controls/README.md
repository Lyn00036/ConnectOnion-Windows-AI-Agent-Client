# Controls/

Reusable `UserControl`s, grouped by the surface they belong to.

**All files here keep the flat `ConnectOnion.WinUIClient.Controls` namespace regardless of
subfolder.** This is deliberate. A control's CLR namespace is what every consuming XAML file
names in its `xmlns:controls="using:ConnectOnion.WinUIClient.Controls"` declaration (24 files do)
and what `x:Class` in each `.xaml` must match. Making the namespace follow the folder would mean
editing every one of those for zero behavioural gain, and a mismatch surfaces only at XAML-compile
time. The folders are physical organisation; the namespace is the contract.

A `.xaml` and its `.xaml.cs` must always live in the same folder.

| Folder | Contains |
|---|---|
| `Chat/` | The composer (text, attachments, speech), tool-activity cards, offline bar |
| `Agents/` | Agent identity UI: the add-agent form, the avatar |
| `Settings/` | The settings overlay and its content panes, plus keyboard-shortcut editing |
| `Shell/` | Window chrome: sidebar, in-app notification host, about overlay |
| `Primitives/` | Generic building blocks with no feature knowledge: markdown, highlighted text, icon+label, the shared disclosure animation |

`Primitives/` is the one with a rule attached: nothing in it may reference a view model or a
feature-specific model. If a primitive needs that, it belongs in one of the other folders.
