using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using CatFish.Core;
using CatFish.Features.Automation;
using CatFish.Features.Popup;
using CatFish.Features.Simulation;
using CatFish.Features.Transparency;
using CatFish.UI;
using CatFish;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _passed++;
        Console.WriteLine("PASS: " + message);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }

    private static void Run(string[] args)
    {
        if (args is ["--native-targets"]) { AutomationRegression.RunNativeTargets(); return; }
        if (args is ["--popup-targets"]) { PopupRegression.RunTargets(); return; }
        if (args is ["--secondary", var instanceName])
        {
            using var secondary = new SingleInstanceService(instanceName);
            Check(!secondary.IsPrimary, "second process does not become the primary instance");
            Check(secondary.NotifyExistingInstanceAsync().GetAwaiter().GetResult(), "second process notifies existing instance");
            return;
        }

        var application = new Application
        {
            Resources = (ResourceDictionary)Application.LoadComponent(new Uri("/CatFish;component/UI/Styles.xaml", UriKind.Relative))
        };
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        typeof(SettingsStore).GetField("_path", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(AppController.SettingsStore, Path.Combine(AppContext.BaseDirectory, "regression-app-settings.json"));
        if (args is ["--main-window"])
        {
            MainWindowRegression.Run(Check, Render);
            Console.WriteLine($"TOTAL: {_passed} main window checks passed."); return;
        }
        if (args is ["--simulation"])
        {
            SimulationRegression.Run(Check, Render);
            Console.WriteLine($"TOTAL: {_passed} simulation checks passed."); return;
        }
        if (args is ["--tray-preview"])
        {
            var previewMain = new MainWindow();
            using var tray = new TrayService(previewMain);
            var menu = (System.Windows.Forms.ContextMenuStrip)typeof(TrayService).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
            void Preview(System.Windows.Forms.ToolStripDropDown dropdown, string name)
            {
                dropdown.Size = dropdown.GetPreferredSize(System.Drawing.Size.Empty);
                dropdown.CreateControl(); dropdown.PerformLayout();
                using var bitmap = new System.Drawing.Bitmap(dropdown.Width, dropdown.Height);
                dropdown.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, name));
                Console.WriteLine("RENDER: " + name);
            }
            var automation = (System.Windows.Forms.ToolStripMenuItem)menu.Items["automation"]!;
            var switching = (System.Windows.Forms.ToolStripMenuItem)automation.DropDownItems["switch-tasks"]!;
            Check(!menu.IsHandleCreated && !automation.DropDown.IsHandleCreated && !switching.DropDown.IsHandleCreated,
                "first-open regression starts without pre-created menu handles or preview layout");
            var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var originalPointer = System.Windows.Forms.Cursor.Position;
            void PumpMenu(int milliseconds)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            try
            {
                menu.Show(new System.Drawing.Point(area.Left + 40, area.Top + 40));
                System.Windows.Forms.Cursor.Position = menu.PointToScreen(new System.Drawing.Point(automation.Bounds.Left + 20, automation.Bounds.Top + automation.Height / 2));
                PumpMenu(180);
                Check(automation.DropDown.Visible, "submenu opens within the local short hover delay");
                Check(automation.DropDown.Left == menu.Right - 1, "submenu touches the parent menu without a blank gap");
                switching.Select(); switching.ShowDropDown();
                Check(switching.DropDown.Left == automation.DropDown.Right - 1, "second-level submenu also touches its parent");
                var bounds = System.Drawing.Rectangle.Union(System.Drawing.Rectangle.Union(menu.Bounds, automation.DropDown.Bounds), switching.DropDown.Bounds);
                using (var combined = new System.Drawing.Bitmap(bounds.Width, bounds.Height))
                {
                    using var graphics = System.Drawing.Graphics.FromImage(combined);
                    graphics.Clear(System.Drawing.Color.White);
                    foreach (var popup in new System.Windows.Forms.ToolStripDropDown[] { menu, automation.DropDown, switching.DropDown })
                    {
                        using var bitmap = new System.Drawing.Bitmap(popup.Width, popup.Height);
                        popup.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, popup.Size));
                        graphics.DrawImageUnscaled(bitmap, popup.Left - bounds.Left, popup.Top - bounds.Top);
                    }
                    combined.Save(Path.Combine(AppContext.BaseDirectory, "tray-compact-cascade.png"));
                }
                var music = menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(item => item.Text == "音乐播放");
                System.Windows.Forms.Cursor.Position = menu.PointToScreen(new System.Drawing.Point(music.Bounds.Left + 20, music.Bounds.Top + music.Height / 2));
                PumpMenu(180);
                Check(music.DropDown.Visible && !automation.DropDown.Visible, "moving between menu groups switches submenus promptly");
                PumpMenu(400);
                Check(music.DropDown.Visible && !automation.DropDown.Visible, "native hover timers do not reopen the old submenu");
                menu.Close();
                menu.Show(new System.Drawing.Point(area.Right - menu.Width - 2, area.Top + 40));
                automation.ShowDropDown(); switching.ShowDropDown();
                Check(automation.DropDown.Right == menu.Left + 1, "submenu opens flush to the left when the screen edge blocks the right");
                Check(switching.DropDown.Right == automation.DropDown.Left + 1, "nested submenu keeps the left-opening direction");
                menu.Close();
                using var freshTray = new TrayService(previewMain);
                var freshMenu = (System.Windows.Forms.ContextMenuStrip)typeof(TrayService).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(freshTray)!;
                var freshAutomation = (System.Windows.Forms.ToolStripMenuItem)freshMenu.Items["automation"]!;
                var freshSwitching = (System.Windows.Forms.ToolStripMenuItem)freshAutomation.DropDownItems["switch-tasks"]!;
                Check(!freshMenu.IsHandleCreated && !freshAutomation.DropDown.IsHandleCreated && !freshSwitching.DropDown.IsHandleCreated,
                    "screen-edge regression also starts with entirely fresh menu handles");
                for (var opening = 1; opening <= 2; opening++)
                {
                    freshMenu.Show(new System.Drawing.Point(area.Right - 2, area.Bottom - 2));
                    freshAutomation.ShowDropDown(); freshSwitching.ShowDropDown();
                    Check(freshAutomation.DropDown.Right == freshMenu.Left + 1 && freshSwitching.DropDown.Right == freshAutomation.DropDown.Left + 1,
                        $"bottom-right tray placement is flush on opening {opening}, including both submenu levels");
                    freshMenu.Close();
                }
            }
            finally { menu.Close(); System.Windows.Forms.Cursor.Position = originalPointer; }
            automation.Select(); Preview(menu, "tray-themed.png");
            switching.Select(); Preview(automation.DropDown, "tray-themed-automation.png");
            switching.DropDownItems[0].Select(); Preview(switching.DropDown, "tray-themed-switching.png");
            previewMain.ExitRequested = true; previewMain.Close(); return;
        }
        if (args is ["--emergency-stop"])
        {
            EmergencyStopRegression.Run(Check);
            Console.WriteLine($"TOTAL: {_passed} emergency stop checks passed."); return;
        }
        if (args is ["--hotkeys"])
        {
            HotkeyRegression.Run(Check, Render);
            Console.WriteLine($"TOTAL: {_passed} hotkey checks passed."); return;
        }
        if (args is ["--automation"])
        {
            AutomationRegression.Run(Check, Render); AppController.Shutdown();
            Console.WriteLine($"TOTAL: {_passed} automation checks passed."); return;
        }
        if (args is ["--transparency"])
        {
            TransparencyRegression.Run(Check, Render);
            Console.WriteLine($"TOTAL: {_passed} transparency checks passed."); return;
        }
        if (args is ["--music-local", var musicFolder])
        {
            MusicRegression.RunLocal(Check, Render, musicFolder);
            Console.WriteLine($"TOTAL: {_passed} local music playback checks passed."); return;
        }
        if (args is ["--music"] or ["--music-live"])
        {
            MusicRegression.Run(Check, Render, args[0] == "--music-live");
            Console.WriteLine($"TOTAL: {_passed} music checks passed."); return;
        }
        if (args is ["--popup"])
        {
            PopupRegression.Run(Check, Render); AppController.Shutdown();
            Console.WriteLine($"TOTAL: {_passed} popup checks passed."); return;
        }
        if (args is ["--txt-reader"])
        {
            TxtReaderRegression.Run(Check, Render); AppController.Shutdown();
            Console.WriteLine($"TOTAL: {_passed} TXT reader checks passed."); return;
        }
        Check(typeof(CatFish.App).Assembly.GetName().Name == "CatFish", "application assembly uses the CatFish name");
        using (var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/CatFish;component/Assets/CatFish.ico")).Stream)
        using (var icon = new System.Drawing.Icon(iconStream, 32, 32))
            Check(icon.Width == 32 && icon.Height == 32, "embedded CatFish tray icon decodes at 32 pixels");
        var migrationRoot = Path.Combine(AppContext.BaseDirectory, "branding-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            SettingsStore.MigrateLegacySettings(migrationRoot);
            Check(!Directory.Exists(migrationRoot), "fresh installation does not create legacy configuration");
            var legacyPath = Path.Combine(migrationRoot, "FishDesk", "settings.json");
            var currentPath = Path.Combine(migrationRoot, "CatFish", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            const string legacyJson = """{"ScrollRows":7,"TxtReader":{"Position":42}}""";
            File.WriteAllText(legacyPath, legacyJson);
            SettingsStore.MigrateLegacySettings(migrationRoot);
            Check(File.ReadAllText(currentPath) == legacyJson && File.ReadAllText(legacyPath) == legacyJson,
                "rename copies the entire legacy configuration and preserves the original");
            File.WriteAllText(currentPath, "{}");
            SettingsStore.MigrateLegacySettings(migrationRoot);
            Check(File.ReadAllText(currentPath) == "{}", "existing CatFish configuration takes precedence over legacy settings");
        }
        finally { if (Directory.Exists(migrationRoot)) Directory.Delete(migrationRoot, true); }
        var defaults = new AppSettings().Hotkeys;
        Check(defaults["DecreaseOpacity"] == "Ctrl+Shift+J" && defaults["IncreaseOpacity"] == "Ctrl+Shift+K"
            && defaults["ResetOpacity"] == "Ctrl+Shift+L" && !defaults.ContainsKey("OpenTransparency"), "new shortcut defaults");

        // Exercise the real settings loader using an isolated file, leaving user settings untouched.
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "legacy-settings.json");
        try
        {
            File.WriteAllText(settingsPath, """{"Hotkeys":{"OpenTransparency":"Ctrl+Alt+Shift+T","ToggleMain":"Ctrl+Alt+M"}}""");
            var store = new SettingsStore();
            typeof(SettingsStore).GetField("_path", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(store, settingsPath);
            var migrated = store.Load().Hotkeys;
            Check(!migrated.ContainsKey("OpenTransparency") && migrated["DecreaseOpacity"] == "Ctrl+Shift+J"
                && migrated["IncreaseOpacity"] == "Ctrl+Shift+K" && migrated["ResetOpacity"] == "Ctrl+Shift+L"
                && !migrated.ContainsKey("ToggleMain"), "legacy settings migrate and remove retired bindings");
            var saved = new AppSettings { ScrollRows = 10, ScrollRandomRows = false, ScrollRandomPause = false };
            Task.WhenAll(Enumerable.Range(0, 6).Select(_ => store.SaveAsync(saved))).GetAwaiter().GetResult();
            var loaded = store.Load();
            Check(loaded.ScrollRows == 10 && !loaded.ScrollRandomRows && !loaded.ScrollRandomPause,
                "random scroll settings persist and concurrent saves produce valid configuration");
        }
        finally { File.Delete(settingsPath); }

        // Hidden HWNDs exercise actual user32 alpha behavior without changing desktop focus.
        using var first = new System.Windows.Forms.Form { Text = "CatFish opacity regression A" };
        using var second = new System.Windows.Forms.Form { Text = "CatFish opacity regression B" };
        var firstStyle = NativeMethods.GetWindowLongPtr(first.Handle, NativeMethods.GwlExStyle);
        var secondStyle = NativeMethods.GetWindowLongPtr(second.Handle, NativeMethods.GwlExStyle);
        var appearance = new WindowAppearanceService();
        try
        {
            Check(appearance.TrySetOpacity(first.Handle, .9, out _), "set native window to 90%");
            Check(NativeMethods.GetLayeredWindowAttributes(first.Handle, out _, out var alpha, out _) && alpha == 230, "native alpha matches 90%");
            Check(appearance.TrySetOpacity(second.Handle, .5, out _) && NativeMethods.GetLayeredWindowAttributes(first.Handle, out _, out alpha, out _) && alpha == 230,
                "windows retain independent opacity");
            Check(appearance.TrySetOpacity(first.Handle, -1, out _) && NativeMethods.GetLayeredWindowAttributes(first.Handle, out _, out alpha, out _) && alpha == 0, "lower bound is 0%");
            Check(appearance.TrySetOpacity(first.Handle, 2, out _) && NativeMethods.GetLayeredWindowAttributes(first.Handle, out _, out alpha, out _) && alpha == 255, "upper bound and full opacity are 100%");
            appearance.RestoreAll();
            Check(NativeMethods.GetWindowLongPtr(first.Handle, NativeMethods.GwlExStyle) == firstStyle
                && NativeMethods.GetWindowLongPtr(second.Handle, NativeMethods.GwlExStyle) == secondStyle, "restore original window styles");

            NativeMethods.SetWindowLongPtr(first.Handle, NativeMethods.GwlExStyle, new nint(firstStyle.ToInt64() | NativeMethods.WsExLayered));
            NativeMethods.SetLayeredWindowAttributes(first.Handle, 0x00FF00, 128, NativeMethods.LwaAlpha | NativeMethods.LwaColorKey);
            Check(appearance.TrySetOpacity(first.Handle, .9, out _)
                && NativeMethods.GetLayeredWindowAttributes(first.Handle, out var color, out alpha, out var flags)
                && color == 0x00FF00 && alpha == 230 && (flags & NativeMethods.LwaColorKey) != 0, "preserve existing color-key transparency");
            appearance.RestoreAll();
            Check(NativeMethods.GetLayeredWindowAttributes(first.Handle, out color, out alpha, out flags)
                && color == 0x00FF00 && alpha == 128 && flags == (NativeMethods.LwaAlpha | NativeMethods.LwaColorKey), "restore existing layered alpha and color key");
            Check(!appearance.TrySetOpacity(nint.Zero, .5, out var error) && error.Length > 0, "invalid window returns a useful error");
        }
        finally { appearance.RestoreAll(); }

        var nativeStyle = NativeMethods.GetWindowLongPtr(second.Handle, NativeMethods.GwlStyle);
        try
        {
            Check(!WindowDiscoveryService.IsUserWindow(second.Handle), "hidden background window is not selectable");
            // Set the flags on test HWNDs without displaying or activating a desktop window.
            NativeMethods.SetWindowLongPtr(second.Handle, NativeMethods.GwlStyle, new nint(nativeStyle.ToInt64() | 0x10000000L));
            Check(WindowDiscoveryService.IsUserWindow(second.Handle), "normal open application window is selectable");
            NativeMethods.SetWindowLongPtr(second.Handle, NativeMethods.GwlExStyle, new nint(secondStyle.ToInt64() | NativeMethods.WsExToolWindow));
            Check(!WindowDiscoveryService.IsUserWindow(second.Handle), "floating tool window is excluded");
            NativeMethods.SetWindowLongPtr(second.Handle, NativeMethods.GwlExStyle, new nint(secondStyle.ToInt64() | NativeMethods.WsExNoActivate));
            Check(!WindowDiscoveryService.IsUserWindow(second.Handle), "non-activating overlay window is excluded");
            NativeMethods.SetWindowLongPtr(second.Handle, NativeMethods.GwlExStyle, secondStyle);
            NativeMethods.SetWindowLongPtr(second.Handle, NativeMethods.GwlStyle, new nint(nativeStyle.ToInt64() | 0x30000000L));
            Check(WindowDiscoveryService.IsUserWindow(second.Handle), "minimized application window remains selectable");
            NativeMethods.SetWindowLongPtr(second.Handle, NativeMethods.GwlStyle, new nint(nativeStyle.ToInt64() | 0x10000000L));
            var cloak = 1;
            if (DwmSetWindowAttribute(second.Handle, 13, ref cloak, sizeof(int)) == 0
                && NativeMethods.DwmGetWindowAttribute(second.Handle, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                Check(!WindowDiscoveryService.IsUserWindow(second.Handle), "DWM-cloaked application is excluded even with WS_VISIBLE");
            else Console.WriteLine("SKIP: DWM cloaking is unavailable for the test window.");
            cloak = 0; DwmSetWindowAttribute(second.Handle, 13, ref cloak, sizeof(int));
            Check(!WindowDiscoveryService.IsUserWindow(NativeMethods.GetShellWindow())
                && !WindowDiscoveryService.IsUserWindow(NativeMethods.GetDesktopWindow()), "desktop shell is excluded");
        }
        finally
        {
            NativeMethods.SetWindowLongPtr(second.Handle, NativeMethods.GwlStyle, nativeStyle);
            NativeMethods.SetWindowLongPtr(second.Handle, NativeMethods.GwlExStyle, secondStyle);
        }
        Check(!new WindowDiscoveryService().GetEligibleWindows(second.Handle).Any(t => t.Handle == second.Handle), "hidden main window is not inserted into application choices");

        // Verify the actual WM_HOTKEY dispatch without reserving global shortcuts.
        var shortcutWindow = new Window();
        new WindowInteropHelper(shortcutWindow).EnsureHandle();
        try
        {
            using var hotkeys = new HotkeyService(shortcutWindow);
            var actions = new List<string>();
            hotkeys.Pressed += actions.Add;
            var wndProc = typeof(HotkeyService).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var id in new[] { 302, 308, 309 })
                wndProc.Invoke(hotkeys, new object[] { nint.Zero, NativeMethods.WmHotkey, new nint(id), nint.Zero, false });
            Check(actions.SequenceEqual(new[] { "DecreaseOpacity", "IncreaseOpacity", "ResetOpacity" }), "WM_HOTKEY dispatches the three distinct opacity actions");
            var parse = typeof(HotkeyService).GetMethod("TryParse", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var (action, key) in new[] { ("DecreaseOpacity", 0x4Au), ("IncreaseOpacity", 0x4Bu), ("ResetOpacity", 0x4Cu) })
            {
                object[] parseArgs = { defaults[action], 0u, 0u };
                Check((bool)parse.Invoke(null, parseArgs)! && (uint)parseArgs[1] == (NativeMethods.ModControl | NativeMethods.ModShift)
                    && (uint)parseArgs[2] == key, $"{action} parses as Ctrl+Shift with the correct virtual key");
            }
        }
        finally { shortcutWindow.Close(); }

        var name = "CatFish.Regression." + Guid.NewGuid().ToString("N");
        var activations = 0;
        using (var primary = new SingleInstanceService(name))
        {
            Check(primary.IsPrimary, "first launch owns the single-instance mutex");
            primary.ActivationRequested += () => Interlocked.Increment(ref activations);
            for (var i = 0; i < 3; i++)
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                start.ArgumentList.Add("--secondary"); start.ArgumentList.Add(name);
                using var child = Process.Start(start)!;
                // Also cover a second launch arriving before the listener is ready.
                if (i == 0) primary.StartListening();
                if (!child.WaitForExit(10000)) { child.Kill(); throw new TimeoutException("Secondary process did not exit."); }
                Check(child.ExitCode == 0, "secondary launch exits after activation: " + child.StandardError.ReadToEnd());
            }
            Check(Volatile.Read(ref activations) == 3, "all repeated launches reach the same primary instance");
        }
        using (var restarted = new SingleInstanceService(name))
            Check(restarted.IsPrimary, "a new launch can become primary after shutdown");

        MainWindowRegression.Run(Check, Render);
        AutomationRegression.Run(Check, Render);
        PopupRegression.Run(Check, Render);
        TxtReaderRegression.Run(Check, Render);
        var main = new CatFish.UI.MainWindow { Left = -5000, Top = -5000 };
        main.CenterOnScreen();
        var work = SystemParameters.WorkArea;
        Check(main.Left == work.Left + Math.Max(0, (work.Width - main.Width) / 2)
            && main.Top == work.Top + Math.Max(0, (work.Height - main.Height) / 2), "main window centers instead of retaining its old position");
        using (var tray = new TrayService(main))
        {
            var menu = (System.Windows.Forms.ContextMenuStrip)typeof(TrayService).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
            var labels = menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Select(item => item.Text).ToArray();
            Check(new[] { "窗口透明", "切换滚动", "系统模拟", "广告弹窗", "单行阅读", "音乐播放" }.All(labels.Contains), "tray menu contains all six tool entries including music");

            typeof(AppController).GetField("_main", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, main);
            application.MainWindow = main;
            var features = (Dictionary<string, Window>)typeof(AppController).GetField("Features", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            foreach (var (label, key) in new[]
            {
                ("窗口透明", "transparency-hint"), ("切换滚动", "switcher"), ("系统模拟", "simulation"), ("快捷键设置", "hotkeys")
            })
            {
                var entry = menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Single(item => item.Text == label);
                if (entry is System.Windows.Forms.ToolStripMenuItem { HasDropDownItems: true } parent)
                    parent.DropDownItems.Cast<System.Windows.Forms.ToolStripItem>().Single(item => item.Text == "程序设置").PerformClick();
                else entry.PerformClick();
                var feature = features[key];
                try
                {
                    Check(feature.IsVisible && feature.Owner is null && !main.IsVisible, $"tray opens {key} without showing the launcher");
                    Check(feature.WindowStyle == WindowStyle.None && feature.ResizeMode == ResizeMode.NoResize
                        && Descendants<Button>((FrameworkElement)feature.Content).Count(b => b.Name == "WindowCloseButton") == 1,
                        $"{key} has custom chrome with only one close button");
                    Render(feature, key + "-v9.png");
                    if (Descendants<TabControl>((FrameworkElement)feature.Content).FirstOrDefault() is { } tabs)
                    {
                        for (var index = 1; index < tabs.Items.Count; index++)
                        {
                            tabs.SelectedIndex = index;
                            Render(feature, key + $"-tab{index}-v9.png");
                        }
                    }
                    if (feature is WindowSwitcherWindow switcher)
                    {
                        var picker = new WindowPickerWindow(switcher, true, Array.Empty<nint>());
                        Check(picker.WindowStyle == WindowStyle.None && picker.ResizeMode == ResizeMode.NoResize,
                            "application picker uses the same custom chrome");
                        var choices = (ListBox)typeof(WindowPickerWindow).GetField("_list", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(picker)!;
                        choices.ItemsSource = new[]
                        {
                            new PreviewChoice { Name = "记事本" }, new PreviewChoice { Name = "资源管理器" }, new PreviewChoice { Name = "浏览器", Selected = true }
                        };
                        Render(picker, "application-picker-v9.png");
                        picker.Close();
                    }
                }
                finally { feature.Close(); }
            }
            menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Single(item => item.Text == "桌面弹窗").PerformClick();
            Check(AdvertisementPopupWindow.ActivePopup is { IsVisible: true } && !main.IsVisible,
                "tray popup entry opens the actual advertisement without showing the launcher");
            AdvertisementPopupWindow.CloseAll();
            var popupSettings = new PopupSettingsWindow();
            Render(popupSettings, "popup-settings-v10.png");
            popupSettings.Close();
        }

        var hint = new TransparencyHintWindow();
        Check(hint.WindowStyle == WindowStyle.None && hint.AllowsTransparency && !hint.ShowInTaskbar,
            "shortcut hint uses a compact borderless window");
        Check(AppController.OpacityStep == 10, "opacity shortcut step is ten percentage points");
        Render(main, "main-v9.png");
        Render(hint, "transparency-hint-v9.png");
        foreach (var (scene, _, _) in SystemSimulationWindow.Scenes)
        {
            var sceneWindow = new SimulationSceneWindow(scene, false, false, false);
            try
            {
                Check(sceneWindow.WindowStyle == WindowStyle.None && sceneWindow.ResizeMode == ResizeMode.NoResize
                    && Descendants<Button>((FrameworkElement)sceneWindow.Content).Count(b => b.Name == "WindowCloseButton") == 1,
                    $"windowed {scene} scene also has custom chrome with only a close button");
            }
            finally { sceneWindow.Close(); }
        }
        AppController.Shutdown();
        Console.WriteLine($"TOTAL: {_passed} checks passed.");
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);

    private sealed class PreviewChoice
    {
        public string Name { get; set; } = "";
        public bool Selected { get; set; }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Render(Window window, string filename)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.Width, double.IsNaN(window.Height) ? double.PositiveInfinity : window.Height));
        content.Arrange(new Rect(0, 0, window.Width, double.IsNaN(window.Height) ? content.DesiredSize.Height : window.Height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, filename));
        encoder.Save(stream);
        Console.WriteLine("RENDER: " + stream.Name);
    }
}
