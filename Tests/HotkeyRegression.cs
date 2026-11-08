using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CatFish.Core;
using CatFish.UI;

internal static class HotkeyRegression
{
    internal static void Run(Action<bool, string> check, Action<Window, string> render)
    {
        var defaults = new AppSettings().Hotkeys;
        var removed = new[] { "ToggleMain", "OpenSwitcher", "NextWindow", "ToggleReading" };
        check(removed.All(action => !defaults.ContainsKey(action)), "retired shortcuts are absent from defaults");
        foreach (var (action, key, modifiers, chord) in new[]
        {
            ("TogglePopup", Key.T, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+T"),
            ("ToggleTxtReader", Key.Y, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+Y"),
            ("TxtPlayback", Key.F4, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+F4"),
            ("TxtPrevious", Key.Up, ModifierKeys.Control, "Ctrl+Up"),
            ("TxtNext", Key.Down, ModifierKeys.Control, "Ctrl+Down"),
            ("MusicPlayback", Key.F5, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+F5"),
            ("MusicPrevious", Key.Left, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+Left"),
            ("MusicNext", Key.Right, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+Right"),
            ("EmergencyStop", Key.X, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+X")
        })
        {
            check(defaults[action] == chord && HotkeyService.FromKey(key, modifiers) == chord, action + " captures the requested key combination");
            var parse = typeof(HotkeyService).GetMethod("TryParse", BindingFlags.NonPublic | BindingFlags.Static)!;
            object[] args = { chord, 0u, 0u };
            check((bool)parse.Invoke(null, args)! && (uint)args[2] == (uint)KeyInterop.VirtualKeyFromKey(key), action + " parses into the correct Windows key code");
        }
        check(HotkeyService.FromKey(Key.LeftCtrl, ModifierKeys.Control) is null
            && HotkeyService.FromKey(Key.A, ModifierKeys.None) is null
            && HotkeyService.FromKey(Key.A, ModifierKeys.Windows) is null, "incomplete and unsupported combinations are rejected");

        var path = Path.Combine(AppContext.BaseDirectory, "hotkey-migration-test.json");
        try
        {
            var old = new AppSettings();
            foreach (var action in removed) old.Hotkeys[action] = "Ctrl+Alt+Shift+M";
            old.Hotkeys["TogglePopup"] = "Ctrl+Alt+Shift+P";
            old.Hotkeys["TxtPrevious"] = "Ctrl+Alt+Shift+PageUp";
            old.Hotkeys["EmergencyStop"] = "Ctrl+Alt+Shift+X";
            old.Hotkeys["ToggleTxtReader"] = "Ctrl+Alt+B";
            old.Hotkeys["TxtNext"] = "";
            old.Hotkeys.Remove("MusicPlayback");
            File.WriteAllText(path, JsonSerializer.Serialize(old));
            var store = new SettingsStore();
            typeof(SettingsStore).GetField("_path", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(store, path);
            var loaded = store.Load();
            check(!loaded.HotkeysDisabled, "existing settings keep global shortcuts enabled by default");
            check(removed.All(action => !loaded.Hotkeys.ContainsKey(action)) && loaded.Hotkeys["TogglePopup"] == "Ctrl+Shift+T"
                && loaded.Hotkeys["TxtPrevious"] == "Ctrl+Up" && loaded.Hotkeys["EmergencyStop"] == "Ctrl+Shift+X"
                && loaded.Hotkeys["MusicPlayback"] == "Ctrl+Alt+F5", "loading old settings upgrades defaults, adds music and removes retired shortcuts");
            check(loaded.Hotkeys["ToggleTxtReader"] == "Ctrl+Alt+B" && loaded.Hotkeys["TxtNext"] == "", "migration preserves custom and disabled shortcuts");
            loaded.Hotkeys["TogglePopup"] = "Ctrl+Alt+Shift+P";
            store.SaveAsync(loaded).GetAwaiter().GetResult();
            check(store.Load().Hotkeys["TogglePopup"] == "Ctrl+Alt+Shift+P", "migration runs only once so later customization persists");
            loaded.HotkeysDisabled = true;
            store.SaveAsync(loaded).GetAwaiter().GetResult();
            var disabled = store.Load();
            check(disabled.HotkeysDisabled && disabled.Hotkeys.OrderBy(pair => pair.Key).SequenceEqual(loaded.Hotkeys.OrderBy(pair => pair.Key)),
                "global shortcut disabling survives a settings reload and preserves every binding");
        }
        finally { File.Delete(path); }

        var host = new Window(); var competing = new Window();
        var hwnd = new WindowInteropHelper(host).EnsureHandle();
        var otherHwnd = new WindowInteropHelper(competing).EnsureHandle();
        var saved = AppController.Settings.Hotkeys;
        var savedDisabled = AppController.Settings.HotkeysDisabled;
        var controllerHotkeys = typeof(AppController).GetField("_hotkeys", BindingFlags.NonPublic | BindingFlags.Static)!;
        var savedService = controllerHotkeys.GetValue(null);
        using (var hotkeys = new HotkeyService(host))
        {
            try
            {
                var actions = new List<string>(); hotkeys.Pressed += actions.Add;
                var dispatch = typeof(HotkeyService).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance)!;
                void Press(int id) => dispatch.Invoke(hotkeys, new object[] { hwnd, NativeMethods.WmHotkey, new nint(id), nint.Zero, false });
                foreach (var id in new[] { 301, 303, 304, 305, 314, 315, 316 }) Press(id);
                check(actions.SequenceEqual(new[] { "MusicPlayback", "MusicPrevious", "MusicNext" }), "music messages dispatch and retired messages do nothing");
                check(!hotkeys.TrySet("MusicPlayback", defaults["TxtPlayback"], out var error) && error.Contains("分配"), "duplicate shortcuts report the conflicting action");
                check(!hotkeys.TrySet("EmergencyStop", "", out _) && hotkeys.TrySet("MusicNext", "", out _), "only optional shortcuts can be disabled");
                AppController.Settings.Hotkeys = defaults.ToDictionary(p => p.Key, _ => "");
                AppController.Settings.Hotkeys["MusicPlayback"] = "Ctrl+Alt+Shift+F12";
                check(hotkeys.RegisterDefaults().Count == 0, "test shortcut registers with Windows");
                const uint mods = NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift;
                check(!NativeMethods.RegisterHotKey(otherHwnd, 900, mods, 0x7B), "registered shortcut is reserved");
                hotkeys.Suspend(); Press(314);
                check(actions.Count == 3 && NativeMethods.RegisterHotKey(otherHwnd, 900, mods, 0x7B), "capture releases Windows registration and suppresses hotkey dispatch");
                NativeMethods.UnregisterHotKey(otherHwnd, 900);
                check(hotkeys.Resume().Count == 0 && !NativeMethods.RegisterHotKey(otherHwnd, 900, mods, 0x7B), "ending capture restores the Windows registration");
                Press(314); check(actions.Count == 4, "hotkey dispatch resumes after capture");
                controllerHotkeys.SetValue(null, hotkeys);
                var originalChord = AppController.Settings.Hotkeys["MusicPlayback"];
                check(AppController.SetHotkeysDisabled(true).Count == 0 && AppController.Settings.Hotkeys["MusicPlayback"] == originalChord
                    && NativeMethods.RegisterHotKey(otherHwnd, 900, mods, 0x7B), "global disabling immediately releases the Windows shortcut and preserves its combination");
                NativeMethods.UnregisterHotKey(otherHwnd, 900);
                Press(314); Press(307);
                check(actions.Count == 4, "global disabling suppresses queued shortcuts including emergency stop");
                hotkeys.Suspend(); hotkeys.Resume();
                check(hotkeys.RegisterDefaults().Count == 0 && NativeMethods.RegisterHotKey(otherHwnd, 900, mods, 0x7B),
                    "ending capture and refreshing registration cannot reactivate globally disabled shortcuts");
                NativeMethods.UnregisterHotKey(otherHwnd, 900);
                check(AppController.TrySetHotkey("MusicPlayback", "Ctrl+Alt+Shift+F11", out _) && NativeMethods.RegisterHotKey(otherHwnd, 900, mods, 0x7A),
                    "editing a binding while disabled saves the new combination without reserving it");
                check(AppController.SetHotkeysDisabled(false).SequenceEqual(new[] { "音乐播放/暂停" }), "reenabling reports shortcuts occupied by another application");
                NativeMethods.UnregisterHotKey(otherHwnd, 900);
                check(hotkeys.RegisterDefaults().Count == 0 && !NativeMethods.RegisterHotKey(otherHwnd, 900, mods, 0x7A),
                    "reenabling registers the preserved customized combination again");
                Press(314); check(actions.Count == 5, "reenabled shortcuts dispatch normally");
            }
            finally
            {
                NativeMethods.UnregisterHotKey(otherHwnd, 900);
                AppController.Settings.Hotkeys = saved;
                AppController.Settings.HotkeysDisabled = savedDisabled;
                controllerHotkeys.SetValue(null, savedService);
            }
        }
        host.Close(); competing.Close();

        var settings = new HotkeySettingsWindow();
        try
        {
            render(settings, "hotkey-settings.png");
            var inputs = Descendants<TextBox>((DependencyObject)settings.Content).ToArray();
            check(inputs.Length == 12 && inputs.All(b => b.IsReadOnly), "all twelve settings use key capture instead of editable strings");
            check(inputs.Single(b => b.Name == "TxtPreviousInput").Text == "Ctrl+↑"
                && inputs.Single(b => b.Name == "MusicNextInput").Text == "Ctrl+Alt+→", "direction keys display as arrows");
            check(Descendants<TextBlock>((DependencyObject)settings.Content).Any(t => t.Text == "显示/隐藏广告弹窗"), "advertisement popup has the requested label");
            var source = new HwndSource(new HwndSourceParameters("key capture regression"));
            using (source)
            {
                var box = inputs.Single(b => b.Name == "TxtPreviousInput");
                box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Delete) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                check(box.Text == "" && (string)box.Tag == "", "Delete clears a captured optional shortcut");
                box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                check(box.Text == "Ctrl+↑", "Escape restores the saved shortcut");
                var emergency = inputs.Single(b => b.Name == "EmergencyStopInput");
                emergency.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Back) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                check(emergency.Text == "Ctrl+Shift+X", "key capture cannot clear emergency stop");
            }
            var disable = Descendants<CheckBox>((DependencyObject)settings.Content).Single(box => box.Name == "DisableHotkeysCheckBox");
            var savedChords = AppController.Settings.Hotkeys.ToDictionary(pair => pair.Key, pair => pair.Value);
            disable.IsChecked = true;
            check(AppController.Settings.HotkeysDisabled && inputs.All(box => !box.IsEnabled)
                && AppController.Settings.Hotkeys.OrderBy(pair => pair.Key).SequenceEqual(savedChords.OrderBy(pair => pair.Key)),
                "the disable checkbox deactivates shortcut editing while preserving existing combinations");
            render(settings, "hotkey-settings-disabled.png");
            Descendants<Button>((DependencyObject)settings.Content).Single(button => button.Content as string == "恢复推荐快捷键")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(AppController.Settings.HotkeysDisabled && disable.IsChecked == true
                && Descendants<TextBox>((DependencyObject)settings.Content).All(box => !box.IsEnabled), "restoring recommended combinations leaves the global disable option selected");
            disable.IsChecked = false;
            check(!AppController.Settings.HotkeysDisabled && Descendants<TextBox>((DependencyObject)settings.Content).All(box => box.IsEnabled),
                "clearing the disable checkbox reactivates shortcut editing");
        }
        finally
        {
            settings.Close(); AppController.Settings.Hotkeys = saved; AppController.Settings.HotkeysDisabled = savedDisabled;
            AppController.SaveSettingsAsync().GetAwaiter().GetResult();
        }

        // A hidden launcher should remain hidden; the old implementation showed and activated it here.
        var mainField = typeof(AppController).GetField("_main", BindingFlags.NonPublic | BindingFlags.Static)!;
        var main = new MainWindow(); mainField.SetValue(null, main);
        var foreground = NativeMethods.GetForegroundWindow();
        AppController.EmergencyStop();
        check(!main.IsVisible && NativeMethods.GetForegroundWindow() == foreground, "emergency stop does not show the launcher or steal foreground focus");
        mainField.SetValue(null, null); main.ExitRequested = true; main.Close();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
