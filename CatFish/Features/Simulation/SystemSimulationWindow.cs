using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using CatFish.Core;
using CatFish.UI;

namespace CatFish.Features.Simulation;

public sealed class SystemSimulationWindow : Window
{
    private readonly CheckBox _fullScreen = new() { Content = "全屏显示场景", IsChecked = AppController.Settings.SimulationFullScreen };
    private readonly CheckBox _randomProgress = new() { Content = "进度随机增长", IsChecked = AppController.Settings.SimulationRandomProgress };
    private readonly CheckBox _animateProgress = new() { Content = "显示缓慢变化的进度", IsChecked = AppController.Settings.SimulationAnimateProgress };
    private readonly TextBlock _status = ViewFactory.Status();

    public SystemSimulationWindow()
    {
        Title = "系统模拟 · CatFish"; Width = 330; Height = 410; MinWidth = 310; MinHeight = 370; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = ViewFactory.Shell(this);
        root.Children.Add(ViewFactory.Card(BuildOptions())); root.Children.Add(ViewFactory.Card(BuildScenes())); root.Children.Add(_status);
        Content = ViewFactory.Frame(this, new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root }, "系统模拟");
        _fullScreen.Checked += (_, _) => SaveSettings(); _fullScreen.Unchecked += (_, _) => SaveSettings();
        _randomProgress.Checked += (_, _) => SaveSettings(); _randomProgress.Unchecked += (_, _) => SaveSettings();
        _animateProgress.Checked += (_, _) => SaveSettings(); _animateProgress.Unchecked += (_, _) => SaveSettings();
    }

    private UIElement BuildOptions()
    {
        var p = new StackPanel(); p.Children.Add(_fullScreen); p.Children.Add(_animateProgress); p.Children.Add(_randomProgress); return p;
    }

    private UIElement BuildScenes()
    {
        var p = new StackPanel();
        p.Children.Add(Scenario("Windows 10 蓝屏", "经典停止错误画面。", "Blue"));
        p.Children.Add(Scenario("Windows 11 黑屏", "新版停止错误画面。", "Black"));
        p.Children.Add(Scenario("系统更新进度", "可缓慢增长到 100%，完成后可关闭或返回。", "Update"));
        p.Children.Add(Scenario("程序无响应提示", "只在 CatFish 中展示模拟提示，不冻结程序。", "Crash"));
        p.Children.Add(ViewFactory.Muted("本地演示 · Esc 退出")); return p;
    }

    private UIElement Scenario(string title, string detail, string id)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 8) }; var button = ViewFactory.Action("打开", (_, _) => Launch(id)); DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
        var text = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, ToolTip = detail }; row.Children.Add(text); return row;
    }

    private void Launch(string id)
    {
        SaveSettings();
        AppController.ShowSimulationScene(id, this); _status.Text = "场景已打开 · Esc 退出";
    }

    private void SaveSettings()
    {
        AppController.Settings.SimulationFullScreen = _fullScreen.IsChecked == true;
        AppController.Settings.SimulationAnimateProgress = _animateProgress.IsChecked == true;
        AppController.Settings.SimulationRandomProgress = _randomProgress.IsChecked == true;
        _ = AppController.SaveSettingsAsync();
    }
}

public sealed class SimulationSceneWindow : Window
{
    private static readonly HashSet<SimulationSceneWindow> OpenScenes = new();
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private readonly DispatcherTimer _spinnerTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly TextBlock _progressText = new();
    private readonly RotateTransform _rotation = new();
    private readonly Random _rng = new();
    private readonly string _scene;
    private readonly bool _random;
    private int _progress = 8;

    public SimulationSceneWindow(string scene, bool fullScreen, bool animateProgress, bool randomProgress)
    {
        _scene = scene; _random = randomProgress;
        Title = scene == "Crash" ? RandomApplicationName() : "CatFish";
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"); Foreground = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = scene == "Blue" ? BlueBrush : Brushes.Black;
        Width = scene == "Crash" ? 460 : 960; Height = scene == "Crash" ? 260 : 540;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        var sceneContent = scene switch { "Blue" => BuildBlueScreen(), "Black" => BuildBlackScreen(), "Update" => BuildUpdateScreen(), _ => BuildCrashDialog() };
        if (scene != "Crash" && fullScreen) { WindowState = WindowState.Maximized; Content = sceneContent; }
        else Content = ViewFactory.Frame(this, new Border { Background = Background, Child = sceneContent }, Title);
        OpenScenes.Add(this);
        _progressTimer.Tick += (_, _) => AdvanceProgress();
        _spinnerTimer.Tick += (_, _) => _rotation.Angle = (_rotation.Angle + 12) % 360;
        if (animateProgress && scene != "Crash") _progressTimer.Start();
        if (scene == "Update") _spinnerTimer.Start();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
        Closed += (_, _) => { _progressTimer.Stop(); _spinnerTimer.Stop(); OpenScenes.Remove(this); };
    }

    private static SolidColorBrush BlueBrush => new(Color.FromRgb(0, 120, 215));
    private static TextBlock Text(string text, double size, Thickness? margin = null) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };

    private UIElement BuildBlueScreen()
    {
        var layout = new Canvas { Width = 1200, Height = 675, Background = BlueBrush };
        var content = new StackPanel { Width = 820 };
        content.Children.Add(Text(":(", 100, new Thickness(0, 0, 0, 12)));
        content.Children.Add(Text("你的电脑遇到问题，需要重新启动。\n我们只收集某些错误信息，然后为你重新启动。", 26));
        _progressText.FontSize = 23; _progressText.Text = _progress + "% 完成"; _progressText.Margin = new Thickness(0, 30, 0, 30); content.Children.Add(_progressText);
        var details = new DockPanel();
        var code = SupportCode(); code.Margin = new Thickness(0, 0, 20, 0); DockPanel.SetDock(code, Dock.Left); details.Children.Add(code);
        var help = new StackPanel();
        help.Children.Add(Text("有关此问题的详细信息和可能的解决方法，请访问\nhttps://www.windows.com/stopcode", 15));
        help.Children.Add(Text("如果致电支持人员，请向他们提供以下信息：\n终止代码：CRITICAL_PROCESS_DIED", 14, new Thickness(0, 18, 0, 0)));
        details.Children.Add(help); content.Children.Add(details);
        Canvas.SetLeft(content, 115); Canvas.SetTop(content, 65); layout.Children.Add(content);
        return new Viewbox { Stretch = Stretch.Uniform, Child = layout };
    }

    private UIElement BuildBlackScreen()
    {
        var layout = new Grid { Margin = new Thickness(50) };
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 800 };
        center.Children.Add(Text("你的设备遇到问题，需要重新启动。", 22));
        _progressText.FontSize = 18; _progressText.Text = _progress + "% 完成"; _progressText.TextAlignment = TextAlignment.Center; _progressText.Margin = new Thickness(0, 34, 0, 0); center.Children.Add(_progressText);
        layout.Children.Add(center);
        var stopCode = Text("终止代码：CRITICAL_PROCESS_DIED (0xEF)", 12);
        stopCode.HorizontalAlignment = HorizontalAlignment.Center; stopCode.VerticalAlignment = VerticalAlignment.Bottom; stopCode.Margin = new Thickness(0, 0, 0, 18);
        layout.Children.Add(stopCode); return layout;
    }

    private UIElement BuildUpdateScreen()
    {
        var root = new Grid();
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var spinner = new Canvas { Width = 36, Height = 36, RenderTransform = _rotation, RenderTransformOrigin = new Point(.5, .5), Margin = new Thickness(0, 0, 0, 26) };
        for (var i = 0; i < 6; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 3.5, Height = 3.5, Fill = Brushes.White, Opacity = 1 - i * .13 };
            var angle = (i * 30 - 90) * Math.PI / 180; Canvas.SetLeft(dot, 16 + Math.Cos(angle) * 14); Canvas.SetTop(dot, 16 + Math.Sin(angle) * 14); spinner.Children.Add(dot);
        }
        center.Children.Add(spinner);
        _progressText.FontSize = 18; _progressText.TextAlignment = TextAlignment.Center; _progressText.Text = $"正在进行更新 {_progress}%"; center.Children.Add(_progressText);
        var keepOn = Text("请保持计算机开启状态。", 18, new Thickness(0, 6, 0, 0)); keepOn.TextAlignment = TextAlignment.Center; center.Children.Add(keepOn);
        root.Children.Add(center);
        var footer = Text("你的计算机可能会重启几次。", 14, new Thickness(0, 0, 0, 65));
        footer.HorizontalAlignment = HorizontalAlignment.Center; footer.VerticalAlignment = VerticalAlignment.Bottom; root.Children.Add(footer); return root;
    }

    private UIElement BuildCrashDialog()
    {
        Background = Brushes.White; Foreground = new SolidColorBrush(Color.FromRgb(32, 32, 32)); FontSize = 12;
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 12) };
        var title = new TextBlock { Text = Title + " 没有响应", FontSize = 19, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Title }; title.Foreground = new SolidColorBrush(Color.FromRgb(0, 80, 150)); body.Children.Add(title);
        var message = Text("如果你继续等待，该程序可能会响应。", 12, new Thickness(0, 12, 0, 12)); body.Children.Add(message);
        body.Children.Add(new ProgressBar { Style = (Style)Application.Current.Resources["BusyProgressStyle"], IsIndeterminate = true, Height = 4, Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 215)), Background = new SolidColorBrush(Color.FromRgb(229, 238, 245)), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 14) });
        foreach (var label in new[] { "关闭程序", "等待程序响应" })
        {
            var button = new Button { Content = "→  " + label, Background = Brushes.White, Foreground = new SolidColorBrush(Color.FromRgb(0, 80, 150)), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 8, 8, 8), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(225, 225, 225)), Margin = new Thickness(0, 0, 0, 6) };
            button.Click += (_, _) => { if (label == "关闭程序") Close(); else message.Text = "正在等待程序响应…"; }; body.Children.Add(button);
        }
        return body;
    }

    private static string RandomApplicationName()
    {
        var apps = new WindowDiscoveryService().GetEligibleWindows(ViewFactory.MainHandle)
            .Where(t => !t.IsCatFish && !t.ProcessName.Equals("CatFish", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(t => t.ProcessId).ToArray();
        if (apps.Length == 0) return "记事本";
        var app = apps[Random.Shared.Next(apps.Length)];
        try
        {
            using var process = Process.GetProcessById(app.ProcessId);
            if (process.MainModule?.FileName is { } path && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } name) return name;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { }
        return app.ApplicationName ?? app.ProcessName;
    }

    private void AdvanceProgress()
    {
        _progress = Math.Min(100, _progress + (_random ? _rng.Next(1, 4) : 1));
        _progressText.Text = _scene == "Update" ? $"正在进行更新 {_progress}%" : $"{_progress}% 完成";
        if (_progress >= 100) { _progressTimer.Stop(); _spinnerTimer.Stop(); }
    }

    private static Image SupportCode()
    {
        // A QR code for the same public support address printed beside it.
        string[] rows =
        {
            "1111111011110010001111111", "1000001000011000101000001", "1011101001010110101011101", "1011101000111100101011101", "1011101000111110101011101",
            "1000001001010010101000001", "1111111010101010101111111", "0000000011000101100000000", "1101101001001000101000001", "1101010110110111100111110",
            "1000101011011101110101001", "1001010000010011101101111", "1000111011111001001000001", "1011100100110101100010010", "1101101100100001010011111",
            "1011010100110001000101101", "1011111111001101111110110", "0000000010000011100010110", "1111111001010100101010001", "1000001000111111100010001",
            "1011101010101011111110010", "1011101011010111011000011", "1011101001111100110011111", "1000001011111010001110111", "1111111011010000111001001"
        };
        var group = new DrawingGroup();
        using (var drawing = group.Open())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 33, 33));
            for (var y = 0; y < rows.Length; y++) for (var x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == '1') drawing.DrawRectangle(BlueBrush, null, new Rect(x + 4, y + 4, 1, 1));
        }
        group.Freeze(); return new Image { Source = new DrawingImage(group), Width = 100, Height = 100, Stretch = Stretch.Fill };
    }

    public static void CloseAll() { foreach (var scene in OpenScenes.ToArray()) scene.Close(); }
}
