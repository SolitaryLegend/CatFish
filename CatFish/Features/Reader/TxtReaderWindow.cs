using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Threading;
using CatFish.Core;
using CatFish.UI;
using Forms = System.Windows.Forms;
using HoverPopup = System.Windows.Controls.Primitives.Popup;

namespace CatFish.Features.Reader;

public sealed class TxtReaderWindow : Window
{
    private readonly TxtReaderService _reader;
    private readonly TxtReaderSurface _surface;
    private bool _arranging, _closed, _positionReady;
    private HwndSource? _source;
    private readonly HoverPopup _toolbar;
    private readonly Border _toolbarPanel;
    private readonly DispatcherTimer _hideToolbar;
    private readonly Button _playButton, _modeButton, _lockButton, _pinButton;
    private readonly ReaderNumberStepper _speedStepper, _fontStepper, _textOpacityStepper, _backgroundOpacityStepper;
    private readonly TextBlock _speedUnit;
    internal HoverPopup HoverToolbar => _toolbar;
    public bool ShowOutline { get; set; }

    public TxtReaderWindow(TxtReaderService reader)
    {
        _reader = reader; Title = "单行阅读 · CatFish";
        ViewFactory.EnableEscapeClose(this);
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        UseLayoutRounding = true; WindowStartupLocation = WindowStartupLocation.Manual;
        _surface = new TxtReaderSurface(reader); Content = _surface;
        _hideToolbar = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _hideToolbar.Tick += HideToolbarTick;
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(ToolbarButton("‹", "上一页", reader.Previous));
        _playButton = ToolbarButton("开始", "开始 / 停止自动阅读", () => { if (reader.IsPlaying) reader.Stop(); else reader.Play(); }); actions.Children.Add(_playButton);
        actions.Children.Add(ToolbarButton("›", "下一页", () => reader.Next()));
        _modeButton = ToolbarButton("静态", "切换静态替换 / 平滑滑动", () => { reader.Settings.Mode = reader.Settings.Mode == "Static" ? "Scroll" : "Static"; reader.RefreshLayout(); }); actions.Children.Add(_modeButton);
        _lockButton = ToolbarButton("锁定", "锁定 / 解锁拖动", () => { reader.Settings.Locked = !reader.Settings.Locked; reader.RefreshLayout(false); }); actions.Children.Add(_lockButton);
        _pinButton = ToolbarButton("置顶", "开启 / 取消置顶", () => { reader.Settings.Topmost = !reader.Settings.Topmost; reader.RefreshLayout(false); }); actions.Children.Add(_pinButton);
        actions.Children.Add(ToolbarButton("设置", "打开单行阅读设置", () => AppController.OpenTool("txt-reader")));
        actions.Children.Add(ToolbarButton("×", "收起浮窗并保存进度", Close));
        _speedStepper = new ReaderNumberStepper("ReaderToolbarSpeed", "速度",
            () => reader.Settings.Mode == "Static" ? reader.Settings.SecondsPerPage : reader.Settings.PixelsPerSecond,
            () => reader.Settings.Mode == "Static" ? 1 : 10, () => reader.Settings.Mode == "Static" ? 30 : 200,
            value =>
            {
                if (_closed) return;
                if (reader.Settings.Mode == "Static") reader.Settings.SecondsPerPage = value; else reader.Settings.PixelsPerSecond = value;
                reader.RefreshLayout(false);
            }, compact: true) { Width = 48 };
        _fontStepper = new ReaderNumberStepper("ReaderToolbarFontSize", "字号", () => reader.Settings.FontSize, () => 10, () => 72,
            value => { if (_closed) return; reader.Settings.FontSize = value; reader.RefreshLayout(); }, compact: true) { Width = 44 };
        const string textOpacity = "文字不透明度", backgroundOpacity = "背景不透明度";
        _textOpacityStepper = OpacityStepper("ReaderToolbarTextOpacity", textOpacity,
            () => reader.Settings.TextOpacity, value => reader.Settings.TextOpacity = value);
        _backgroundOpacityStepper = OpacityStepper("ReaderToolbarBackgroundOpacity", backgroundOpacity,
            () => reader.Settings.BackgroundOpacity, value => reader.Settings.BackgroundOpacity = value);
        var toolbarBackground = new SolidColorBrush(Color.FromRgb(245, 248, 247));
        // The checkerboard image has 48 transparent pixels on each side.
        var opacityIcon = new CroppedBitmap(LoadToolbarIcon("Opacity"), new Int32Rect(48, 48, 160, 160));
        _speedUnit = new TextBlock { Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var adjustments = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
        adjustments.Children.Add(AdjustmentField(new Image { Source = LoadToolbarIcon("Speed") }, "速度", _speedStepper, _speedUnit));
        // Match the visible height of the speed artwork, which has more transparent padding.
        adjustments.Children.Add(AdjustmentField(new Image { Source = LoadToolbarIcon("FontSize") }, "字号", _fontStepper, iconSize: 13));
        adjustments.Children.Add(AdjustmentField(OpacityIcon(opacityIcon, "A", toolbarBackground), textOpacity, _textOpacityStepper, iconSize: 13));
        adjustments.Children.Add(AdjustmentField(OpacityIcon(opacityIcon, "B", toolbarBackground), backgroundOpacity, _backgroundOpacityStepper, iconSize: 13));

        var toolbarContent = new StackPanel(); toolbarContent.Children.Add(actions); toolbarContent.Children.Add(adjustments);
        _toolbarPanel = new Border { Background = toolbarBackground, BorderBrush = new SolidColorBrush(Color.FromRgb(196, 212, 204)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(4), Child = toolbarContent };
        TextElement.SetFontFamily(_toolbarPanel, new FontFamily("Microsoft YaHei UI")); TextElement.SetFontSize(_toolbarPanel, 11);
        TextElement.SetForeground(_toolbarPanel, new SolidColorBrush(Color.FromRgb(41, 60, 53)));
        _toolbar = new HoverPopup { Child = _toolbarPanel, PlacementTarget = _surface, Placement = PlacementMode.Top, VerticalOffset = -4,
            AllowsTransparency = true, StaysOpen = true, Focusable = false };
        _toolbar.Closed += (_, _) => CommitToolbarEdits();
        _surface.MouseEnter += (_, _) => ShowToolbar(); _surface.MouseLeave += (_, _) => ScheduleToolbarHide();
        _toolbarPanel.MouseEnter += (_, _) => _hideToolbar.Stop(); _toolbarPanel.MouseLeave += (_, _) => ScheduleToolbarHide();
        _toolbarPanel.LostKeyboardFocus += (_, _) => ScheduleToolbarHide();
        _toolbarPanel.PreviewMouseDown += (_, e) =>
        {
            if (ToolbarEditor(e.OriginalSource as DependencyObject) is not { } editor)
            { CommitToolbarEdits(); _surface.Focus(); return; }
            // Hovering stays non-activating; an explicit click on an editor must enable native keyboard input.
            if (e.ChangedButton == MouseButton.Left) { Activate(); editor.Focus(); Keyboard.Focus(editor); }
        };
        _toolbarPanel.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _toolbarPanel.KeyDown += (_, e) =>
        {
            // The popup has its own input route, so Escape in its editors must close the reader too.
            if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None || e.IsRepeat) return;
            e.Handled = true; Close();
        };
        var menu = new ContextMenu();
        AddMenu(menu, "阅读设置", () => AppController.OpenTool("txt-reader"));
        AddMenu(menu, "开始 / 停止", () => { if (reader.IsPlaying) reader.Stop(); else reader.Play(); });
        AddMenu(menu, "上一页", reader.Previous); AddMenu(menu, "下一页", () => reader.Next());
        AddMenu(menu, "恢复默认位置及可见样式", ResetVisibleStyle);
        AddMenu(menu, "关闭阅读浮窗", Close); ContextMenu = menu;
        menu.Opened += (_, _) => _toolbar.IsOpen = false; menu.Closed += (_, _) => ScheduleToolbarHide();
        _reader.Changed += ReaderChanged; _reader.Frame += RenderFrame;
        _reader.HideRequested += HideRequested;
        SourceInitialized += (_, _) => { _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); _source?.AddHook(WindowMessages); };
        Loaded += (_, _) => { ApplySettings(); UpdateViewport(); };
        ContentRendered += (_, _) =>
        {
            if (_positionReady || _closed) return;
            // WPF completes its first-show placement after SourceInitialized. Restore only after that layout.
            RestorePosition(); _positionReady = true; SavePosition();
        };
        SizeChanged += (_, _) => { if (!_arranging) { UpdateViewport(); SavePosition(); } };
        PreviewMouseDown += (_, e) => { if (ToolbarEditor(e.OriginalSource as DependencyObject) is null) CommitToolbarEdits(); };
        Deactivated += (_, _) =>
        {
            // An external application can take native focus while WPF retains logical focus in the popup.
            CommitToolbarEdits();
            if (!_toolbarPanel.IsMouseOver && !_surface.IsMouseOver) _toolbar.IsOpen = false;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is TextBoxBase) return;
            switch (e.Key)
            {
                case Key.PageDown: reader.Next(); break;
                case Key.PageUp: reader.Previous(); break;
                case Key.Space: reader.TogglePlayback(); break;
                default: return;
            }
            e.Handled = true;
        };
        MouseWheel += (_, e) => { if (!reader.Settings.MouseWheelPaging) return; if (e.Delta < 0) reader.Next(); else reader.Previous(); e.Handled = true; };
        MouseLeftButtonDown += (_, e) =>
        {
            for (var source = e.OriginalSource as DependencyObject; source is not null; source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
                if (source is ButtonBase or TextBoxBase) return;
            _surface.Focus();
            if (!reader.Settings.Locked && e.ButtonState == MouseButtonState.Pressed)
            {
                _toolbar.IsOpen = false;
                try { DragMove(); } catch (InvalidOperationException) { }
                ConstrainToScreen(); SavePosition(); if (_surface.IsMouseOver) ShowToolbar(); e.Handled = true;
            }
        };
        Closing += (_, _) => { CommitToolbarEdits(); SavePosition(); reader.Pause(); };
        Closed += (_, _) =>
        {
            _closed = true;
            _toolbar.IsOpen = false; _hideToolbar.Stop(); _hideToolbar.Tick -= HideToolbarTick;
            reader.Changed -= ReaderChanged; reader.Frame -= RenderFrame; reader.HideRequested -= HideRequested;
            if (_source is { IsDisposed: false }) _source.RemoveHook(WindowMessages);
        };
        ApplySettings();
        var initialArea = SystemParameters.WorkArea;
        Left = initialArea.Left + Math.Max(0, (initialArea.Width - Width) / 2);
        Top = Math.Max(initialArea.Top, initialArea.Bottom - Height - 50);
    }

    private static Button ToolbarButton(string text, string hint, Action action)
    {
        var button = new Button { Content = text, ToolTip = hint, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(1, 0, 1, 0),
            FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 11, Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(41, 60, 53)), Focusable = false, IsTabStop = false };
        System.Windows.Automation.AutomationProperties.SetName(button, hint);
        button.Click += (_, _) => action(); return button;
    }

    private ReaderNumberStepper OpacityStepper(string name, string caption, Func<double> read, Action<double> change)
    {
        return new ReaderNumberStepper(name, caption, () => read() * 100, () => 0, () => 100, value =>
        {
            if (_closed) return;
            change(value / 100);
            _reader.RefreshLayout(repaginate: false);
        }, compact: true) { Width = 48 };
    }

    private static BitmapImage LoadToolbarIcon(string name) =>
        new(new Uri($"pack://application:,,,/CatFish;component/Assets/ReaderToolbar/{name}.png", UriKind.Absolute));

    private static Grid OpacityIcon(ImageSource source, string letter, Brush background)
    {
        var icon = new Grid();
        icon.Children.Add(new Image { Source = source });
        icon.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = background,
            Child = new TextBlock
            {
                Text = letter, TextAlignment = TextAlignment.Center, FontSize = 8, FontWeight = FontWeights.Bold,
                LineHeight = 8, LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            }
        });
        return icon;
    }

    private static StackPanel AdjustmentField(FrameworkElement icon, string caption, ReaderNumberStepper stepper, TextBlock? unit = null, double iconSize = 16)
    {
        var field = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 0, 3, 0) };
        icon.Width = icon.Height = iconSize;
        icon.Margin = new Thickness(0, 0, 4, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.ToolTip = caption;
        System.Windows.Automation.AutomationProperties.SetName(icon, caption);
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        field.Children.Add(icon);
        field.Children.Add(stepper);
        if (unit is not null) field.Children.Add(unit);
        return field;
    }

    private void CommitToolbarEdits()
    {
        if (_closed) return;
        _speedStepper.Commit(); _fontStepper.Commit();
        _textOpacityStepper.Commit(); _backgroundOpacityStepper.Commit();
    }
    private static TextBox? ToolbarEditor(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox box) return box;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }
    private void ShowToolbar()
    {
        if (_closed || !IsVisible) return;
        _hideToolbar.Stop(); UpdateToolbar();
        _toolbarPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _toolbar.HorizontalOffset = Math.Max(0, (ActualWidth - _toolbarPanel.DesiredSize.Width) / 2);
        _toolbar.IsOpen = true;
    }
    private void ScheduleToolbarHide() { if (!_closed) { _hideToolbar.Stop(); _hideToolbar.Start(); } }
    private void HideToolbarTick(object? sender, EventArgs e)
    {
        _hideToolbar.Stop();
        if (!_surface.IsMouseOver && !_toolbarPanel.IsMouseOver && !_toolbarPanel.IsKeyboardFocusWithin && !_toolbarPanel.IsMouseCaptureWithin && ContextMenu?.IsOpen != true) _toolbar.IsOpen = false;
    }
    private void UpdateToolbar()
    {
        _playButton.Content = _reader.IsPlaying ? "停止" : "开始";
        _modeButton.Content = _reader.Settings.Mode == "Static" ? "静态" : "滑动";
        _lockButton.Content = _reader.Settings.Locked ? "解锁" : "锁定";
        _pinButton.Content = _reader.Settings.Topmost ? "取消置顶" : "置顶";
        _speedUnit.Text = _reader.Settings.Mode == "Static" ? "秒/屏" : "px/s";
        _speedStepper.ToolTip = _reader.Settings.Mode == "Static" ? "每屏秒数：数值越小，阅读越快" : "滑动速度：数值越大，阅读越快";
        _speedStepper.Refresh(); _fontStepper.Refresh();
        _textOpacityStepper.Refresh(); _backgroundOpacityStepper.Refresh();
        _surface.Cursor = _reader.Settings.Locked ? Cursors.Arrow : Cursors.SizeAll;
    }

    private static void AddMenu(ContextMenu menu, string text, Action action)
    { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }

    private void ReaderChanged(object? sender, EventArgs e) { if (!_closed) { ApplySettings(); UpdateToolbar(); _surface.InvalidateVisual(); } }
    private void RenderFrame(object? sender, EventArgs e) => _surface.UpdateFrame();
    private void HideRequested(object? sender, EventArgs e) => Close();

    public void ApplySettings()
    {
        if (_closed) return;
        var settings = _reader.Settings;
        _arranging = true;
        try
        {
            Topmost = settings.Topmost;
            Width = settings.Width; Height = Math.Max(settings.Height, settings.FontSize * settings.LineHeight + 8);
            _surface.Outline = ShowOutline; _surface.InvalidateVisual();
            if (IsLoaded) ConstrainToScreen();
        }
        finally { _arranging = false; }
        UpdateViewport();
    }

    private void UpdateViewport() => _reader.SetViewport(Math.Max(80, Width - 20), _surface.MeasureText);

    private Forms.Screen CurrentScreen() => Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);

    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(NativeMethods.NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    private (double X, double Y) ScreenScale(Forms.Screen screen)
    {
        var bounds = screen.Bounds;
        var monitor = MonitorFromPoint(new NativeMethods.NativePoint { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 }, 2);
        if (GetDpiForMonitor(monitor, 0, out var x, out var y) == 0 && x > 0 && y > 0) return (x / 96d, y / 96d);
        var dpi = VisualTreeHelper.GetDpi(this); return (dpi.DpiScaleX, dpi.DpiScaleY);
    }

    private void MoveToScreen(Forms.Screen screen, double? relativeLeft, double? relativeTop)
    {
        var area = screen.WorkingArea; var scale = ScreenScale(screen);
        var width = Math.Min(area.Width, (int)Math.Ceiling(_reader.Settings.Width * scale.X));
        var height = Math.Min(area.Height, (int)Math.Ceiling(Math.Max(_reader.Settings.Height, _reader.Settings.FontSize * _reader.Settings.LineHeight + 8) * scale.Y));
        var x = relativeLeft is double left ? area.Left + (int)Math.Round(Math.Clamp(left, 0, 1) * Math.Max(0, area.Width - width)) : area.Left + (area.Width - width) / 2;
        var y = relativeTop is double top ? area.Top + (int)Math.Round(Math.Clamp(top, 0, 1) * Math.Max(0, area.Height - height)) : area.Bottom - height - (int)Math.Round(50 * scale.Y);
        _arranging = true;
        try { NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, nint.Zero, x, Math.Max(area.Top, y), width, height, NativeMethods.SwpNoActivate | 4); }
        finally { _arranging = false; }
    }

    private void RestorePosition()
    {
        var settings = _reader.Settings;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.MonitorName) ?? Forms.Screen.PrimaryScreen!;
        if (settings.RelativeLeft is double x && settings.RelativeTop is double y)
            MoveToScreen(screen, x, y);
        else SetDefaultPosition();
        ConstrainToScreen();
    }
    public void SetDefaultPosition()
    {
        _reader.Settings.RelativeLeft = null; _reader.Settings.RelativeTop = null; _reader.Settings.MonitorName = null;
        MoveToScreen(Forms.Screen.PrimaryScreen!, null, null); ConstrainToScreen(); SavePosition();
    }
    public void ResetVisibleStyle()
    {
        _reader.Settings.TextOpacity = 1; _reader.Settings.BackgroundOpacity = 0;
        _reader.Settings.TextColor = "#293C35"; _reader.Settings.FontSize = 22;
        _reader.Settings.Locked = false; Opacity = 1;
        ApplySettings(); SetDefaultPosition(); _reader.RefreshLayout();
    }
    private void ConstrainToScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero || !NativeMethods.GetWindowRect(handle, out var rect)) return;
        var area = CurrentScreen().WorkingArea;
        var width = Math.Min(rect.Right - rect.Left, area.Width); var height = Math.Min(rect.Bottom - rect.Top, area.Height);
        var x = Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - width));
        var y = Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
        if (x != rect.Left || y != rect.Top || width != rect.Right - rect.Left || height != rect.Bottom - rect.Top)
            NativeMethods.SetWindowPos(handle, nint.Zero, x, y, width, height, NativeMethods.SwpNoActivate | 4);
    }
    public void SavePosition()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (_closed || !_positionReady || _arranging || handle == nint.Zero || !NativeMethods.GetWindowRect(handle, out var rect)) return;
        var screen = CurrentScreen(); var area = screen.WorkingArea; var settings = _reader.Settings;
        settings.Width = Width; settings.Height = Height; settings.MonitorName = screen.DeviceName;
        settings.RelativeLeft = Math.Clamp((double)(rect.Left - area.Left) / Math.Max(1, area.Width - (rect.Right - rect.Left)), 0, 1);
        settings.RelativeTop = Math.Clamp((double)(rect.Top - area.Top) / Math.Max(1, area.Height - (rect.Bottom - rect.Top)), 0, 1);
        if (!_closed) _ = _reader.PersistAsync();
    }
    private nint WindowMessages(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x007E or 0x02E0) // WM_DISPLAYCHANGE / WM_DPICHANGED, after WPF updates its scale.
            Dispatcher.BeginInvoke(new Action(() => { if (!_closed) { ConstrainToScreen(); UpdateViewport(); SavePosition(); } }));
        return nint.Zero;
    }
}

internal sealed class TxtReaderSurface : FrameworkElement
{
    private readonly TxtReaderService _reader;
    private readonly Dictionary<string, FormattedText> _cache = new();
    private string _styleKey = "";
    private string _scrollMetricsKey = "";
    private double[] _elementRightEdges = Array.Empty<double>();
    private string _followingMetricsKey = "";
    private int[] _followingOffsets = Array.Empty<int>();
    private int _continuationAllowance = -1;
    private string _continuation = "";
    private readonly TranslateTransform _scrollTransform = new();
    private TxtPage? _renderedPage, _renderedFollowing;
    private string _renderedContinuation = "";
    private bool _renderedScroll;
    public bool Outline { get; set; }
    public TxtReaderSurface(TxtReaderService reader) { _reader = reader; Focusable = true; }

    private FormattedText Format(string text)
    {
        var settings = _reader.Settings; var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var key = $"{settings.FontFamily}|{settings.FontSize}|{settings.Bold}|{settings.TextColor}|{dpi}";
        if (key != _styleKey) { _styleKey = key; _cache.Clear(); _scrollMetricsKey = ""; }
        if (_cache.TryGetValue(text, out var formatted)) return formatted;
        Brush color;
        try { color = (Brush)new BrushConverter().ConvertFromInvariantString(settings.TextColor)!; }
        catch { color = new SolidColorBrush(Color.FromRgb(41, 60, 53)); }
        FontFamily family;
        try { family = new FontFamily(settings.FontFamily); } catch { family = new FontFamily("Microsoft YaHei UI"); }
        formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, settings.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            settings.FontSize, color, dpi);
        if (_cache.Count > 260) _cache.Clear();
        _cache[text] = formatted; return formatted;
    }
    public double MeasureText(string text) => Format(text).WidthIncludingTrailingWhitespace;

    public void UpdateFrame()
    {
        _scrollTransform.X = -_reader.ScrollOffset;
        if (_reader.Settings.Mode == "Scroll" && _renderedScroll
            && ReferenceEquals(_renderedPage, _reader.CurrentPage) && ReferenceEquals(_renderedFollowing, _reader.FollowingPage)
            && (_reader.FollowingPage is null || _renderedContinuation == ScrollContinuation(_reader.CurrentPage!.Text, _reader.FollowingPage.Text)))
            return;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var settings = _reader.Settings; var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        // Layered-window alpha-zero pixels pass native mouse messages through. One alpha step keeps
        // the plate visually transparent while making its entire compact rectangle interactive.
        var hitTestAlpha = (byte)Math.Max(1, Math.Round(settings.BackgroundOpacity * 255));
        drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(hitTestAlpha, 255, 255, 255)), null, bounds);
        if (Outline) drawing.DrawRectangle(null, new Pen(Brushes.SeaGreen, 1), new Rect(.5, .5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)));
        _renderedPage = _reader.CurrentPage; _renderedFollowing = _reader.FollowingPage;
        _renderedScroll = settings.Mode == "Scroll";
        if (_reader.CurrentPage is not { } page) return;
        drawing.PushClip(new RectangleGeometry(new Rect(10, 0, Math.Max(0, ActualWidth - 20), ActualHeight)));
        drawing.PushOpacity(settings.TextOpacity);
        var current = Format(page.Text);
        var x = settings.Alignment switch { "Center" => Math.Max(0, (_reader.ViewportWidth - current.WidthIncludingTrailingWhitespace) / 2), "Right" => Math.Max(0, _reader.ViewportWidth - current.WidthIncludingTrailingWhitespace), _ => 0 };
        if (settings.Mode == "Scroll")
        {
            // Retain the text drawing between frames. Only the fractional translation changes.
            _scrollTransform.X = -_reader.ScrollOffset;
            drawing.PushTransform(_scrollTransform);
            Draw(drawing, current, 0);
            if (_reader.FollowingPage is { } next)
            {
                var continuation = ScrollContinuation(page.Text, next.Text);
                _renderedContinuation = continuation;
                if (continuation.Length > 0)
                    Draw(drawing, Format(continuation), Math.Min(_reader.ViewportWidth, current.WidthIncludingTrailingWhitespace) + 24);
            }
            drawing.Pop();
        }
        else Draw(drawing, current, x);
        drawing.Pop(); drawing.Pop();
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters parameters) => new PointHitTestResult(this, parameters.HitPoint);

    private string ScrollContinuation(string current, string following)
    {
        if (_scrollMetricsKey != current)
        {
            var offsets = StringInfo.ParseCombiningCharacters(current);
            _elementRightEdges = new double[offsets.Length];
            for (var i = 0; i < offsets.Length; i++)
                _elementRightEdges[i] = MeasureText(current[..(i + 1 < offsets.Length ? offsets[i + 1] : current.Length)]);
            _scrollMetricsKey = current;
            _continuationAllowance = -1;
        }
        var exited = 0;
        while (exited < _elementRightEdges.Length && _elementRightEdges[exited] <= _reader.ScrollOffset) exited++;
        var allowance = Math.Max(0, _reader.Settings.MaxCharacters - (_elementRightEdges.Length - exited));
        if (_followingMetricsKey != following)
        {
            _followingMetricsKey = following; _followingOffsets = StringInfo.ParseCombiningCharacters(following);
            _continuationAllowance = -1;
        }
        if (_continuationAllowance != allowance)
        {
            _continuationAllowance = allowance;
            _continuation = allowance >= _followingOffsets.Length ? following : following[.._followingOffsets[allowance]];
        }
        return _continuation;
    }

    private void Draw(DrawingContext drawing, FormattedText text, double x)
    {
        // An exceptionally wide single grapheme must still fit without being split or clipped.
        var scale = Math.Min(1, _reader.ViewportWidth / Math.Max(1, text.WidthIncludingTrailingWhitespace));
        drawing.PushTransform(new TranslateTransform(10 + x, Math.Max(0, (ActualHeight - text.Height * scale) / 2)));
        drawing.PushTransform(new ScaleTransform(scale, scale)); drawing.DrawText(text, new Point()); drawing.Pop(); drawing.Pop();
    }
}
