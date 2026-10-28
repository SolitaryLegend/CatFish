using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using CatFish.Core;
using CatFish.UI;

namespace CatFish.Features.Popup;

public sealed class AdvertisementPopupWindow : Window
{
    private static readonly HashSet<AdvertisementPopupWindow> OpenPopups = new();
    private readonly AppSettings _settings;
    private readonly ExternalWindowHost _host = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(12, 32, 12, 38) };
    private readonly TextBlock _status = new() { Text = "拖动应用窗口到广告上松开，或点击「选择」", Foreground = Brushes.White, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _caption = new() { Text = "广告", Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _hint;
    private readonly DockPanel _toolbar = new() { Background = new SolidColorBrush(Color.FromRgb(78, 22, 24)), LastChildFill = true };
    private readonly StackPanel _menu = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Hidden };
    private readonly Image _background = new() { Stretch = Stretch.Fill };
    private readonly ProcessAudioMute _audio = new();
    private readonly Button _muteButton;
    private string? _backgroundId;
    private DateTime _nextAudioRefresh;
    private readonly DispatcherTimer _frameTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private WindowDropMonitor? _dropMonitor;
    private VideoEdgeOverlay? _overlay;
    private Task<bool>? _miniTask;
    private WindowTarget? _target;
    private WindowTarget? _miniSource;
    private WindowTarget? _closeTarget;
    private DispatcherOperation? _overlayRefresh;
    private bool _restoreOnly;
    private bool _restoreFailed;
    private bool _closed;
    private int _remaining;
    public static AdvertisementPopupWindow? ActivePopup { get; private set; }
    internal ExternalWindowHost Host => _host;
    internal Image AdvertisementBackground => _background;
    internal bool HoverControlsVisible => _menu.Visibility == Visibility.Visible && _hint.Visibility == Visibility.Visible;
    internal ProcessAudioMute Audio => _audio;

    public AdvertisementPopupWindow(AppSettings settings)
    {
        _settings = settings; Title = "广告弹窗 · CatFish";
        _background.Source = PopupBackgrounds.Load(settings.PopupBackground); _backgroundId = settings.PopupBackground;
        _muteButton = ToolbarButton("静音", ToggleMute);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Background = Brushes.Black;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new GridLength(PopupLayout.TitleBarHeight) }); layout.RowDefinitions.Add(new());
        var close = ToolbarButton("×", Close);
        DockPanel.SetDock(close, Dock.Right); _toolbar.Children.Add(close);
        DockPanel.SetDock(_menu, Dock.Right); _toolbar.Children.Add(_menu);
        foreach (var (label, action) in new (string, Action)[]
        {
            ("选择", SelectWindow),
            ("还原", RestoreApp), ("背景", CycleBackground), ("设置", () => AppController.ShowFeature("popup", () => new PopupSettingsWindow()))
        })
        {
            _menu.Children.Add(ToolbarButton(label, action));
        }
        _menu.Children.Insert(1, _muteButton);
        _caption.Margin = new Thickness(8, 0, 0, 0); _toolbar.Children.Add(_caption);
        _toolbar.MouseLeftButtonDown += (_, eventArgs) =>
        {
            if (_settings.PopupLocked || eventArgs.OriginalSource is not (DockPanel or TextBlock)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
            _settings.PopupUseCustomPosition = true; _settings.PopupCustomLeft = Left; _settings.PopupCustomTop = Top;
            _ = AppController.SaveSettingsAsync();
        };
        layout.Children.Add(_toolbar);
        var page = new Grid { ClipToBounds = true };
        _background.Opacity = settings.PopupOpacity; page.Children.Add(_background);
        page.Children.Add(_host);
        _hint = new Border { Background = new SolidColorBrush(Color.FromArgb(205, 65, 16, 20)), Padding = new Thickness(10, 7, 10, 7),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8), Child = _status, Visibility = Visibility.Hidden, IsHitTestVisible = false };
        page.Children.Add(_hint); Grid.SetRow(page, 1); layout.Children.Add(page); Content = layout;
        AllowDrop = true; DragOver += (_, args) => { args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; args.Handled = true; };
        Drop += FileDropped;
        PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { Close(); args.Handled = true; } };
        Loaded += (_, _) =>
        {
            ApplySettings();
            _dropMonitor = new WindowDropMonitor((handle, point) => Dispatcher.BeginInvoke(() => AcceptWindowDrop(handle, point)));
            _frameTimer.Start(); ResetAutoHide();
        };
        LocationChanged += (_, _) => ScheduleOverlayRefresh(); SizeChanged += (_, _) => ScheduleOverlayRefresh();
        DpiChanged += (_, _) => ScheduleOverlayRefresh();
        _host.SizeChanged += (_, _) => ScheduleOverlayRefresh();
        _background.SizeChanged += (_, _) => ScheduleOverlayRefresh();
        _frameTimer.Tick += (_, _) =>
        {
            UpdateHoverControls();
            if (_target is null) return;
            if (!_host.Session.IsAlive) { ClearApp(); SetStatus("目标窗口已关闭，可重新选择应用。"); return; }
            _host.Session.Resize(_host.Handle);
            if (_audio.IsMuted && DateTime.UtcNow >= _nextAudioRefresh)
            {
                _nextAudioRefresh = DateTime.UtcNow.AddSeconds(1);
                if (!_audio.Refresh(out var message)) SetStatus(message);
                UpdateMuteButton();
            }
        };
        _hideTimer.Tick += (_, _) => { if (!IsMouseOver && _target is null && --_remaining <= 0) Close(); };
        MouseEnter += (_, _) => _hideTimer.Stop(); MouseLeave += (_, _) => ResetAutoHide();
        Closing += (_, args) =>
        {
            _restoreFailed = false;
            _closeTarget = !_restoreOnly && _settings.PopupCloseSelectedApplication ? _target ?? _miniSource : null;
            if (!_host.Session.Detach(out var error, restoreForClose: _closeTarget is not null))
            { _restoreFailed = true; _closeTarget = null; args.Cancel = true; SetStatus(error); _hint.Visibility = Visibility.Visible; }
        };
        Closed += (_, _) =>
        {
            _closed = true; _overlayRefresh?.Abort(); _audio.Dispose(); _dropMonitor?.Dispose(); _frameTimer.Stop(); _hideTimer.Stop(); _overlay?.Close(); _overlay = null;
            _host.Dispose(); OpenPopups.Remove(this); if (ActivePopup == this) ActivePopup = null;
            if (_closeTarget is { } target && NativeMethods.IsWindow(target.Handle)
                && NativeMethods.GetWindowThreadProcessId(target.Handle, out var processId) != 0 && processId == target.ProcessId)
                NativeMethods.PostMessage(target.Handle, 0x10, nint.Zero, nint.Zero);
            _closeTarget = null;
        };
        OpenPopups.Add(this);
    }

    public static void ShowGlobal()
    {
        if (ActivePopup is null) { ActivePopup = new(AppController.Settings); ActivePopup.Show(); }
        else { ActivePopup.Show(); ActivePopup.Activate(); }
    }
    public static void ToggleGlobal()
    {
        if (ActivePopup is { IsVisible: true }) ActivePopup.Close(); else ShowGlobal();
    }
    public static void CloseAll()
    {
        foreach (var popup in OpenPopups.ToArray())
        {
            popup._restoreOnly = true;
            try { popup.Close(); }
            finally { popup._restoreOnly = false; }
        }
    }
    internal static void ApplyOpenSettings() { foreach (var popup in OpenPopups.ToArray()) popup.ApplySettings(); }

    private static Button ToolbarButton(string label, Action action)
    {
        var button = new Button { Content = label, Height = 26, MinWidth = label == "×" ? 28 : 42,
            Margin = new Thickness(1, 2, 1, 2), Padding = new Thickness(4, 0, 4, 0), FontSize = 11,
            Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        button.Click += (_, _) => action(); return button;
    }

    private void UpdateHoverControls()
    {
        if (!IsVisible || !NativeMethods.GetCursorPos(out var cursor)) return;
        var start = _toolbar.PointToScreen(new Point(0, 0));
        var end = _toolbar.PointToScreen(new Point(_toolbar.ActualWidth, _toolbar.ActualHeight));
        var visibility = cursor.X >= start.X && cursor.X < end.X && cursor.Y >= start.Y && cursor.Y < end.Y
            ? Visibility.Visible : Visibility.Hidden;
        _menu.Visibility = visibility; _hint.Visibility = _restoreFailed || _miniSource is not null ? Visibility.Visible : visibility;
    }

    public void ApplySettings()
    {
        Width = Math.Clamp(_settings.PopupWidth, PopupLayout.MinWidth, 900); Height = Math.Clamp(_settings.PopupHeight, PopupLayout.MinHeight, 700);
        if (_backgroundId != _settings.PopupBackground)
        { _background.Source = PopupBackgrounds.Load(_settings.PopupBackground); _backgroundId = _settings.PopupBackground; }
        _background.Opacity = _settings.PopupOpacity;
        PositionOnMonitor(); UpdateLayout(); _host.Session.Resize(_host.Handle); UpdateOverlay(); ResetAutoHide();
    }

    private void SelectWindow()
    {
        var picker = new WindowPickerWindow(this, false, _target is null ? [] : [_target.Handle], "选择嵌入窗口", false);
        if (picker.ShowDialog() == true && picker.SelectedTargets.FirstOrDefault() is { } selected) TryEmbed(selected);
    }

    public bool TryEmbed(WindowTarget target)
    {
        if (target.ProcessId == Environment.ProcessId) { SetStatus("请选择其他应用窗口。"); return false; }
        _host.Visibility = Visibility.Visible; UpdateLayout();
        if (!_host.Session.Attach(target, _host.Handle, out var error))
        {
            if (!_host.Session.IsAlive) { _audio.Restore(); UpdateMuteButton(); _target = null; _host.Visibility = Visibility.Collapsed; _overlay?.Hide(); }
            SetStatus(error); return false;
        }
        _restoreFailed = false; _audio.Restore(); UpdateMuteButton(); _target = target;
        _hideTimer.Stop(); _overlay ??= new VideoEdgeOverlay(this); UpdateOverlay();
        SetStatus("已嵌入，可直接操作；「还原」将应用移回屏幕中央。"); return true;
    }

    private void RestoreApp()
    {
        var target = _target;
        if (!_host.Session.Detach(out var error, restoreForClose: true))
        { _restoreFailed = true; SetStatus(error); _hint.Visibility = Visibility.Visible; return; }
        ClearApp();
        if (target is null || !NativeMethods.IsWindow(target.Handle)
            || NativeMethods.GetWindowThreadProcessId(target.Handle, out var processId) == 0 || processId != target.ProcessId) return;
        NativeMethods.ShowWindow(target.Handle, NativeMethods.SwRestore);
        if (!NativeMethods.GetWindowRect(target.Handle, out var bounds)) return;
        var workArea = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var width = Math.Min(workArea.Width, Math.Max(1, bounds.Right - bounds.Left));
        var height = Math.Min(workArea.Height, Math.Max(1, bounds.Bottom - bounds.Top));
        if (!NativeMethods.SetWindowPos(target.Handle, nint.Zero, workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2, width, height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoCopyBits | NativeMethods.SwpFrameChanged | 0x4))
        { _restoreFailed = true; SetStatus("应用已移出，但无法居中，请手动移动窗口。"); _hint.Visibility = Visibility.Visible; return; }
        NativeMethods.RedrawWindow(target.Handle, nint.Zero, nint.Zero,
            NativeMethods.RdwInvalidate | NativeMethods.RdwErase | NativeMethods.RdwAllChildren | NativeMethods.RdwFrame);
        NativeMethods.SetForegroundWindow(target.Handle);
    }

    public void ClearApp()
    {
        _miniSource = null;
        if (!_host.Session.Detach(out var error)) { _restoreFailed = true; SetStatus(error); _hint.Visibility = Visibility.Visible; return; }
        _restoreFailed = false;
        _audio.Restore(); UpdateMuteButton();
        _target = null; _overlay?.Hide(); _host.Visibility = Visibility.Collapsed; _caption.Text = "广告";
        SetStatus("拖动应用窗口到广告上松开，或点击「选择」"); ResetAutoHide();
    }

    internal void ToggleMute()
    {
        if (_target is null) { SetStatus("请先选择应用窗口，再使用静音。"); return; }
        _audio.SetMuted(_target.ProcessId, !_audio.IsMuted, out var message);
        UpdateMuteButton(); SetStatus(message);
    }
    private void UpdateMuteButton()
    {
        _muteButton.Content = _audio.IsMuted ? "取消静音" : "静音";
        _muteButton.ToolTip = _audio.IsMuted ? (_audio.SessionCount > 0 ? "恢复目标应用原来的声音状态" : "静音已开启，等待应用音频会话") : "静音当前应用及其音频子进程";
    }
    internal void CycleBackground()
    {
        var index = PopupBackgrounds.Items.ToList().FindIndex(background => background.Id == _settings.PopupBackground);
        _settings.PopupBackground = PopupBackgrounds.Items[(index + 1) % PopupBackgrounds.Items.Count].Id;
        ApplySettings(); PopupSettingsWindow.RefreshBackgroundSelection(); _ = AppController.SaveSettingsAsync();
    }

    internal void AcceptWindowDrop(nint handle, NativeMethods.NativePoint point)
    {
        if (_closed || !IsVisible || !NativeMethods.GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds)
            || point.X < bounds.Left || point.X >= bounds.Right || point.Y < bounds.Top || point.Y >= bounds.Bottom) return;
        var target = new WindowDiscoveryService().GetEligibleWindows(includeFloating: true).FirstOrDefault(window => window.Handle == handle);
        if (target is not null) TryEmbed(target);
    }

    private void FileDropped(object sender, DragEventArgs args)
    {
        args.Handled = true;
        if (args.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var path = paths.FirstOrDefault(File.Exists);
        if (path is null) return;
        if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        { SetStatus("请拖动已打开的窗口到广告上；快捷方式请用「选择」匹配运行中的窗口。"); return; }
        var matches = new WindowDiscoveryService().GetEligibleWindows(includeFloating: true).Where(window => window.ProcessName.Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) TryEmbed(matches[0]); else SelectWindow();
    }

    private async Task TryMiniPlayerAsync()
    {
        if (_miniSource is not null || _miniTask is { IsCompleted: false }) { SetStatus("仍在等待平台小窗；可点击「还原」取消等待。"); return; }
        if (_target is not { } target) { SelectWindow(); return; }
        if (PlatformMiniPlayer.IsMiniPlayer(target)) { SetStatus("当前选中的已经是平台小窗。"); return; }
        if (!_host.Session.Detach(out var error, restoreForClose: true))
        { _restoreFailed = true; SetStatus(error); _hint.Visibility = Visibility.Visible; return; }
        ClearApp(); _miniSource = target; _hideTimer.Stop();
        SetStatus("已还原应用，正在查找平台的小窗 / 画中画入口…");
        _hint.Visibility = Visibility.Visible;
        try
        {
            await Task.Delay(250);
            if (_closed || _miniSource != target || _target is not null) return;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));
            _miniTask = Task.Run(() => PlatformMiniPlayer.TryInvoke(target.Handle, cancellation.Token));
            var completed = await Task.WhenAny(_miniTask, Task.Delay(2500));
            if (_closed || _miniSource != target || _target is not null) { cancellation.Cancel(); return; }
            var invoked = completed == _miniTask && await _miniTask;
            cancellation.Cancel();
            SetStatus(invoked ? "已调用入口，等待平台小窗；若未出现，请在应用内手动开启。"
                : "未找到可调用入口。请在应用内开启「小窗播放」；30 秒内识别到小窗会自动接入。也可点「选择」。");
            for (var attempt = 0; attempt < 120; attempt++)
            {
                await Task.Delay(250);
                if (_closed || _miniSource != target || _target is not null) return;
                var mini = new WindowDiscoveryService().GetEligibleWindows(includeFloating: true)
                    .FirstOrDefault(window => window.ProcessId == target.ProcessId && PlatformMiniPlayer.IsMiniPlayer(window));
                if (mini is not null)
                {
                    if (TryEmbed(mini)) SetStatus("已接入平台小窗。");
                    return;
                }
            }
            SetStatus("未识别到平台小窗，应用保留在桌面。请开启小窗后点击「选择」；普通窗口缩放不等于小窗模式。");
            _restoreFailed = true;
        }
        finally
        {
            if (_miniSource == target) _miniSource = null;
            if (!_closed) ResetAutoHide();
        }
    }

    private void SetStatus(string message) { _status.Text = message; _hint.ToolTip = message; }
    private void UpdateOverlay()
    {
        if (_overlay is not null && IsLoaded && _target is not null) _overlay.UpdateBounds(_host, _background, _settings);
    }
    private void ScheduleOverlayRefresh()
    {
        if (_closed || _overlay is null || _target is null) return;
        _overlayRefresh?.Abort();
        _overlayRefresh = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _overlayRefresh = null;
            if (_closed || _target is null) return;
            UpdateLayout(); _host.Session.Resize(_host.Handle); UpdateOverlay();
        }));
    }
    private void ResetAutoHide()
    {
        _hideTimer.Stop(); _remaining = Math.Clamp(_settings.PopupAutoHideSeconds, 0, 600);
        if (_remaining > 0 && _target is null && _miniSource is null && !_restoreFailed && !IsMouseOver) _hideTimer.Start();
    }
    private void PositionOnMonitor()
    {
        var monitors = MonitorService.GetMonitors();
        var physical = monitors.Count == 0 ? SystemParameters.WorkArea : monitors[Math.Clamp(_settings.PopupMonitorIndex, 0, monitors.Count - 1)].WorkArea;
        var source = PresentationSource.FromVisual(this); var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var start = transform.Transform(physical.TopLeft); var end = transform.Transform(physical.BottomRight);
        var bounds = new Rect(start, end);
        if (_settings.PopupAspectRatioLocked)
        {
            var imageHeight = Height - PopupLayout.TitleBarHeight;
            var scale = Math.Min(1, Math.Min(bounds.Width / Width, Math.Max(1, bounds.Height - PopupLayout.TitleBarHeight) / imageHeight));
            Width *= scale; Height = imageHeight * scale + PopupLayout.TitleBarHeight;
        }
        else { Width = Math.Min(Width, bounds.Width); Height = Math.Min(Height, bounds.Height); }
        Left = _settings.PopupCorner.EndsWith("Left", StringComparison.Ordinal) ? bounds.Left : bounds.Right - Width;
        Top = _settings.PopupCorner.StartsWith("Top", StringComparison.Ordinal) ? bounds.Top : bounds.Bottom - Height;
        if (_settings.PopupUseCustomPosition && _settings.PopupCustomLeft is double left && _settings.PopupCustomTop is double top)
        { Left = Math.Clamp(left, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width)); Top = Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - Height)); }
    }
}
