using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CatFish.Core;
using CatFish.Features.Simulation;
using CatFish.UI;
using Forms = System.Windows.Forms;

internal static class SimulationRegression
{
    internal static void Run(Action<bool, string> check, Action<Window, string> render)
    {
        string[] expected = ["Windows 10 蓝屏", "Windows 11 黑屏", "系统更新进度",
            "Windows 10 开机动画", "Windows 11 开机动画", "网易UU远程隐私屏保"];
        var settings = new SystemSimulationWindow();
        render(settings, "simulation.png");
        var labels = Descendants<TextBlock>((DependencyObject)settings.Content).Select(text => text.Text).ToArray();
        var openButtons = Descendants<Button>((DependencyObject)settings.Content).Count(button => (string?)button.Content == "打开");
        check(labels.Where(expected.Contains).SequenceEqual(expected) && !labels.Contains("程序无响应提示") && openButtons == expected.Length,
            "simulation settings offers the six current scenes in order");
        settings.Close();

        using (var tray = new TrayService(new MainWindow()))
        {
            var menu = (Forms.ContextMenuStrip)typeof(TrayService).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
            var simulations = menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "系统模拟");
            check(simulations.DropDownItems.Cast<Forms.ToolStripItem>().Skip(1).Select(item => item.Text).SequenceEqual(expected),
                "tray menu exposes the same simulation scenes");
        }

        foreach (var scene in new[] { "Boot10", "Boot11" })
        {
            var preview = new SimulationSceneWindow(scene, fullScreen: false, animateProgress: false, randomProgress: false);
            try
            {
                ShowOffscreen(preview);
                var rotations = Descendants<Canvas>((DependencyObject)preview.Content)
                    .Select(canvas => canvas.RenderTransform).OfType<RotateTransform>().ToArray();
                var rotation = rotations[0];
                var angle = rotation.Angle;
                var speeds = new double[9];
                for (var index = 0; index < speeds.Length; index++)
                {
                    var start = Stopwatch.GetTimestamp();
                    Pump();
                    var nextAngle = rotation.Angle;
                    speeds[index] = (nextAngle - angle + 360) % 360 / Stopwatch.GetElapsedTime(start).TotalSeconds;
                    angle = nextAngle;
                }
                check(rotations.All(item => item.HasAnimatedProperties) && speeds.Max() > Math.Max(30, 2 * speeds.Min()),
                    $"{scene} spinner accelerates and decelerates independently of progress settings");
                render(preview, scene + ".png");
                preview.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(preview)!, 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Pump();
                check(!preview.IsVisible && rotations.All(item => !item.HasAnimatedProperties), $"Esc closes {scene} and stops its animation");
            }
            finally { if (preview.IsVisible) preview.Close(); }
        }

        var privacy = new SimulationSceneWindow("UuPrivacy", fullScreen: true, animateProgress: true, randomProgress: true);
        try
        {
            check(privacy.WindowState == WindowState.Maximized && privacy.WindowStyle == WindowStyle.None,
                "privacy preview supports borderless fullscreen");
            render(privacy, "uu-privacy.png");
            var content = (DependencyObject)privacy.Content;
            var text = Descendants<TextBlock>(content).Select(label => label.Text);
            check(Descendants<Viewbox>(content).Any(view => AutomationProperties.GetName(view) == "网易UU远程")
                && text.SequenceEqual(new[] { "本机已开启黑屏防护，当前正在被远程控制。" }),
                "privacy scene displays the supplied brand and exact protection text without a footer");
        }
        finally { SimulationSceneWindow.CloseAll(); }
    }

    private static void ShowOffscreen(Window window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -5000;
        window.Top = -5000;
        window.Show();
        window.UpdateLayout();
        Pump();
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
