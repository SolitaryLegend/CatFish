using System.IO;
using System.Windows;
using CatFish.Features.Automation;
using CatFish.Features.Popup;
using CatFish.Features.Reader;
using CatFish.Features.Music;
using CatFish.Features.Simulation;
using CatFish.Features.Transparency;
using CatFish.UI;

namespace CatFish.Core;

public static class AppController
{
    public const int OpacityStep = 10;
    private static readonly SettingsStore Store = new();
    private static readonly WindowAppearanceService Appearance = new();
    private static readonly Dictionary<string, Window> Features = new();
    private static HotkeyService? _hotkeys;
    private static MainWindow? _main;
    private static TrayService? _tray;
    private static TxtReaderService? _txtReader;
    private static MusicPlayerService? _music;
    private static bool _isEmergencyStopping;
    public static MusicPlayerService Music => _music ??= new(Settings.Music);
    public static void ToggleMusicPlayback()
    {
        if (_music?.Queue.Count > 0) _ = _music.ToggleAsync();
        else OpenTool("music");
    }
    private static TxtReaderWindow? _readerWindow;
    public static TxtReaderService TxtReader => _txtReader ??= new(Settings.TxtReader, () => Store.SaveAsync(Settings));
    public static TxtReaderWindow? ReaderWindow => _readerWindow;
    public static AppSettings Settings { get; private set; } = new();
    public static MainWindow? MainWindow => _main;
    public static WindowAppearanceService WindowAppearance => Appearance;
    public static SettingsStore SettingsStore => Store;
    public static WindowAutomationService Automation { get; internal set; } = new();
    public static bool StartSwitching() => Automation.StartSwitching(SwitchOptions.FromSettings(Settings));
    public static bool StartScrolling() => Automation.StartScrolling(ScrollOptions.FromSettings(Settings));

    public static void InitializeSettings() => Settings = Store.Load();

    public static void Initialize(MainWindow main)
    {
        _main = main;
        _tray ??= new TrayService(main);
        main.Topmost = Settings.MainWindowTopmost;
        _hotkeys?.Dispose();
        _hotkeys = new HotkeyService(main);
        _hotkeys.Pressed += HandleHotkey;
        var conflicts = _hotkeys.RegisterDefaults();
        if (conflicts.Count > 0) main.StatusText.Text = "快捷键有冲突：" + string.Join("、", conflicts);
        if (Settings.TxtReader.ShowOnStartup && _txtReader is null && Settings.TxtReader.LastFile is not null)
            _ = RestoreTxtReaderOnStartupAsync();
    }

    public static IReadOnlyList<string> ResetHotkeys()
    {
        Settings.Hotkeys = new AppSettings().Hotkeys;
        Settings.HotkeyLayoutVersion = 1;
        if (_main is null) return Array.Empty<string>();
        _hotkeys?.Dispose(); _hotkeys = new HotkeyService(_main); _hotkeys.Pressed += HandleHotkey;
        var conflicts = _hotkeys.RegisterDefaults();
        _ = SaveSettingsAsync();
        return conflicts;
    }

    public static void OpenTool(string tool)
    {
        switch (tool)
        {
            case "transparency": ShowFeature("transparency-hint", () => new TransparencyHintWindow()); break;
            case "switcher": ShowFeature("switcher", () => new WindowSwitcherWindow()); break;
            case "simulation": ShowFeature("simulation", () => new SystemSimulationWindow()); break;
            case "popup": AdvertisementPopupWindow.ShowGlobal(); break;
            case "txt-reader": ShowFeature("txt-reader", () => new TxtReaderSettingsWindow()); break;
            case "music": ShowFeature("music", () => new MusicPlayerWindow()); break;
        }
    }

    public static void SelectAutomationPrograms(bool switching)
    {
        var selected = switching ? Automation.SwitchTargets.Select(target => target.Handle)
            : Automation.ScrollTarget is { } target ? new[] { target.Handle } : Array.Empty<nint>();
        var picker = new WindowPickerWindow(null, switching, selected);
        SetFeatureOwner(picker);
        if (picker.ShowDialog() != true) return;
        if (switching) Automation.SetSwitchTargets(picker.SelectedTargets);
        else Automation.SetScrollTarget(picker.SelectedTargets.FirstOrDefault());
    }

    public static void ShowSimulationScene(string scene, Window? owner = null)
    {
        var window = new SimulationSceneWindow(scene, Settings.SimulationFullScreen,
            Settings.SimulationAnimateProgress, Settings.SimulationRandomProgress) { Owner = owner };
        window.Show(); window.Activate();
    }

    public static void ShowFeature(string key, Func<Window> create)
    {
        if (_main is null) return;
        if (Features.TryGetValue(key, out var open) && open.IsLoaded)
        {
            SetFeatureOwner(open);
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Show(); open.Activate(); return;
        }
        var window = create(); SetFeatureOwner(window); Features[key] = window;
        window.Closed += (_, _) => { if (Features.TryGetValue(key, out var found) && ReferenceEquals(found, window)) Features.Remove(key); };
        window.Show(); window.Activate();
    }

    private static void SetFeatureOwner(Window window)
    {
        // A tray launch must remain independent from a hidden or minimized launcher.
        window.Owner = _main is { IsVisible: true, WindowState: not WindowState.Minimized } ? _main : null;
        if (!window.IsVisible)
            window.WindowStartupLocation = window.Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
    }

    private static void HandleHotkey(string action)
    {
        switch (action)
        {
            case "DecreaseOpacity": AdjustForegroundOpacity(-OpacityStep); break;
            case "IncreaseOpacity": AdjustForegroundOpacity(OpacityStep); break;
            case "ResetOpacity": AdjustForegroundOpacity(null); break;
            case "TogglePopup": PopupSettingsWindow.TogglePreviewGlobal(); break;
            case "ToggleTxtReader": ToggleTxtReader(); break;
            case "TxtPrevious": if (_readerWindow is not null) TxtReader.Previous(); break;
            case "TxtNext": if (_readerWindow is not null) TxtReader.Next(); break;
            case "TxtPlayback": if (_readerWindow is not null) TxtReader.TogglePlayback(); break;
            case "MusicPlayback": ToggleMusicPlayback(); break;
            case "MusicPrevious": if (_music is not null) _ = _music.PreviousAsync(); break;
            case "MusicNext": if (_music is not null) _ = _music.NextAsync(); break;
            case "EmergencyStop": EmergencyStop(); break;
        }
    }

    private static void AdjustForegroundOpacity(int? change)
    {
        if (Appearance.TryChangeForegroundOpacity(change, out var target, out var percent, out var error))
            OpacityToastWindow.ShowFor(target, percent);
        else
        {
            OpacityToastWindow.CloseCurrent();
            if (_main is not null) _main.StatusText.Text = error;
        }
    }

    public static void EmergencyStop()
    {
        if (_isEmergencyStopping) return;
        _isEmergencyStopping = true;
        try
        {
            // Stop feature activity and floating output before closing their control windows.
            _txtReader?.EmergencyStop();
            _music?.EmergencyStop();
            WindowSwitcherWindow.EmergencyStopAll();
            PopupSettingsWindow.EmergencyStop();
            SimulationSceneWindow.CloseAll();
            _readerWindow?.Close();
            OpacityToastWindow.CloseCurrent();
            Appearance.RestoreAll();

            // Include independent tray-launched windows and dialogs, not just registered features.
            var windows = Application.Current?.Windows.Cast<Window>()
                .Where(window => window != _main && window is not AdvertisementPopupWindow)
                .OrderByDescending(OwnerDepth).ToArray() ?? [];
            foreach (var window in windows)
            {
                if (!Application.Current!.Windows.Cast<Window>().Contains(window)) continue;
                window.Close();
                // A validation/save cancellation can retain edits without leaving a window onscreen.
                if (window.IsVisible) window.Hide();
            }

            // Keep the existing close-to-tray behavior and global shortcuts available.
            if (_main is not null)
            {
                _main.Opacity = 1;
                _main.StatusText.Text = "紧急停止完成：功能已停止，子窗口已关闭。";
                _main.Close();
            }
        }
        finally { _isEmergencyStopping = false; }
    }

    private static int OwnerDepth(Window window)
    {
        var depth = 0;
        for (var owner = window.Owner; owner is not null; owner = owner.Owner) depth++;
        return depth;
    }

    public static void ReturnToMain()
    {
        if (_main is null) return;
        _main.Show(); if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal; _main.Activate();
    }

    public static async Task SaveSettingsAsync()
    {
        try { await Store.SaveAsync(Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (_main is not null) _main.StatusText.Text = $"设置保存失败：{ex.Message}"; }
    }

    public static void Shutdown()
    {
        OpacityToastWindow.CloseCurrent();
        _music?.Dispose(); _music = null;
        _readerWindow?.Close(); _readerWindow = null;
        _txtReader?.Dispose(); _txtReader = null;
        try { Store.SaveAsync(Settings).GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _tray?.Dispose(); _tray = null;
        WindowSwitcherWindow.EmergencyStopAll(); PopupSettingsWindow.EmergencyStop(closeSelectedApplications: false); SimulationSceneWindow.CloseAll();
        Appearance.RestoreAll(); _hotkeys?.Dispose(); _hotkeys = null;
    }

    public static void RequestExit()
    {
        if (_main is not null) { _main.ExitRequested = true; _main.Close(); }
        Application.Current?.Shutdown();
    }

    internal static void BeginHotkeyCapture() => _hotkeys?.Suspend();
    internal static IReadOnlyList<string> EndHotkeyCapture() => _hotkeys?.Resume() ?? Array.Empty<string>();

    public static IReadOnlyList<string> SetHotkeysDisabled(bool disabled)
    {
        Settings.HotkeysDisabled = disabled;
        var conflicts = _hotkeys?.RegisterDefaults() ?? Array.Empty<string>();
        _ = SaveSettingsAsync(); return conflicts;
    }

    public static bool TrySetHotkey(string action, string shortcut, out string error)
    {
        if (_hotkeys is null) { error = "快捷键服务尚未启动。"; return false; }
        if (!_hotkeys.TrySet(action, shortcut, out error)) return false;
        Settings.Hotkeys[action] = shortcut; _ = SaveSettingsAsync(); return true;
    }

    public static void ShowTxtReader()
    {
        if (_isEmergencyStopping) return;
        if (TxtReader.Document is null) return;
        if (_readerWindow is null)
        {
            var window = new TxtReaderWindow(TxtReader) { ShowOutline = Features.TryGetValue("txt-reader", out var settingsWindow) && settingsWindow.IsVisible }; _readerWindow = window;
            window.Closed += (_, _) => { if (ReferenceEquals(_readerWindow, window)) _readerWindow = null; };
        }
        _readerWindow.Show();
    }
    public static void ToggleTxtReader()
    {
        if (_readerWindow is not null) _readerWindow.Close();
        else if (TxtReader.Document is not null) ShowTxtReader();
        else OpenTool("txt-reader");
    }
    public static void RestoreTxtReader()
    {
        ShowTxtReader();
        if (_readerWindow is not null) _readerWindow.ResetVisibleStyle(); else OpenTool("txt-reader");
    }
    public static void SetTxtReaderOutline(bool enabled)
    {
        if (_readerWindow is null) return;
        _readerWindow.ShowOutline = enabled; _readerWindow.ApplySettings();
    }
    private static async Task RestoreTxtReaderOnStartupAsync()
    {
        var path = Settings.TxtReader.LastFile;
        if (path is null) return;
        var record = Settings.TxtReader.RecentFiles.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (await TxtReader.OpenAsync(path, record?.Encoding ?? "Auto")) ShowTxtReader();
        else if (_main is not null) _main.StatusText.Text = TxtReader.Status;
    }
}
