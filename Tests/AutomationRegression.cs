using System.Diagnostics;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;
using CatFish.Core;
using CatFish.Features.Automation;
using CatFish.UI;
using Forms = System.Windows.Forms;

internal static class AutomationRegression
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now += TimeSpan.FromSeconds(seconds);
    }
    private sealed class Platform : IWindowAutomationPlatform
    {
        public nint ForegroundWindow { get; set; }
        public HashSet<nint> Unavailable { get; } = new();
        public HashSet<nint> Blocked { get; } = new();
        public List<nint> Activations { get; } = new();
        public List<int> Wheels { get; } = new();
        public Action? OnActivate { get; set; }
        public bool IsAvailable(WindowTarget target) => !Unavailable.Contains(target.Handle);
        public bool TryActivate(nint handle)
        {
            Activations.Add(handle); OnActivate?.Invoke();
            if (Blocked.Contains(handle)) return false;
            ForegroundWindow = handle; return true;
        }
        public bool SendWheel(nint target, int delta) { Wheels.Add(delta); return ForegroundWindow == target; }
    }

    public static void Run(Action<bool, string> check, Action<Window, string> render)
    {
        var platform = new Platform(); var clock = new Clock();
        var service = new WindowAutomationService(platform, clock, new Random(17));
        var a = new WindowTarget(new nint(11), "应用 A", "A", 1);
        var b = new WindowTarget(new nint(12), "应用 B", "B", 2);
        var c = new WindowTarget(new nint(13), "应用 C", "C", 3);
        var ordered = new SwitchOptions(10, 10, "Ordered", true);
        check(!service.StartSwitching(ordered) && !service.IsSwitching, "missing selection does not enter running state");
        service.SetSwitchTargets(new[] { a, b, c });
        check(service.StartSwitching(ordered) && platform.ForegroundWindow == a.Handle, "start switches immediately");
        clock.Advance(10); service.Tick(); clock.Advance(10); service.Tick(); clock.Advance(10); service.Tick();
        check(platform.Activations.SequenceEqual(new[] { a.Handle, b.Handle, c.Handle, a.Handle }), "scheduled switching continues through multiple complete cycles");
        platform.Blocked.Add(b.Handle); clock.Advance(10); service.Tick();
        check(platform.ForegroundWindow == c.Handle && service.IsSwitching, "failed activation skips to another selected app without stopping");
        platform.Blocked.UnionWith(new[] { a.Handle, b.Handle, c.Handle }); clock.Advance(10); service.Tick();
        check(service.IsSwitching && service.SwitchStatus.Contains("未能获得焦点"), "activation failures remain visible and keep the schedule alive");
        platform.Blocked.Clear(); platform.Unavailable.UnionWith(new[] { a.Handle, b.Handle, c.Handle }); clock.Advance(10); service.Tick();
        check(service.IsSwitching && service.SwitchStatus.Contains("等待可用"), "temporarily unavailable windows do not stop automation");
        platform.Unavailable.Clear(); clock.Advance(10); service.Tick();
        check(service.IsSwitching && service.SwitchStatus.Contains("当前"), "switching resumes when selected windows become available");
        service.StopSwitching(); var attempts = platform.Activations.Count; clock.Advance(100); service.Tick();
        check(platform.Activations.Count == attempts, "stop prevents further scheduled activations");
        platform.ForegroundWindow = b.Handle;
        service.StartSwitching(ordered with { Mode = "Random" });
        check(platform.ForegroundWindow != b.Handle, "random switching avoids the current app when other choices exist");
        service.StopSwitching(); service.StartSwitching(ordered with { Mode = "Manual" }); attempts = platform.Activations.Count;
        clock.Advance(100); service.Tick(); check(platform.Activations.Count == attempts, "manual mode does not schedule automatic switches"); service.StopSwitching();

        var scroll = new ScrollOptions(1, 1, 10, true, "Down", false, false);
        check(!service.StartScrolling(scroll) && !service.IsScrolling, "scrolling requires a selected target");
        service.SetScrollTarget(a); platform.ForegroundWindow = a.Handle; service.StartScrolling(scroll);
        for (var index = 0; index < 200; index++) { clock.Advance(1); service.Tick(); }
        check(platform.Wheels.All(delta => delta <= -120 && delta >= -1200) && platform.Wheels.Distinct().Count() == 10,
            "random row setting covers 1 through 10 inclusive with correct downward direction");
        service.StopScrolling(); platform.Wheels.Clear(); service.StartScrolling(scroll with { RandomRows = false });
        for (var index = 0; index < 4; index++) { clock.Advance(1); service.Tick(); }
        check(platform.Wheels.SequenceEqual(Enumerable.Repeat(-1200, 4)), "disabling random rows uses the exact configured count");
        service.StopScrolling(); platform.Wheels.Clear(); service.StartScrolling(scroll with { RandomRows = false, Direction = "Alternate" });
        for (var index = 0; index < 4; index++) { clock.Advance(1); service.Tick(); }
        check(platform.Wheels.SequenceEqual(new[] { -1200, 1200, -1200, 1200 }), "alternating scroll starts downward and reverses on successful sends");
        platform.ForegroundWindow = b.Handle; var sent = platform.Wheels.Count; clock.Advance(1); service.Tick();
        check(service.IsScrolling && platform.Wheels.Count == sent, "background target is skipped when activation is disabled");
        platform.Unavailable.Add(a.Handle); clock.Advance(1); service.Tick();
        check(service.IsScrolling, "an unavailable content target does not change requested running state");
        platform.Unavailable.Clear(); service.StopScrolling();
        service.StartScrolling(scroll with { MayActivate = true }); platform.OnActivate = service.StopScrolling;
        clock.Advance(1); service.Tick(); platform.OnActivate = null;
        check(!service.IsScrolling && platform.Wheels.Count == sent, "stopping during activation cancels the pending scroll");

        var original = AppController.Automation; AppController.Automation = service;
        try
        {
            platform.ForegroundWindow = a.Handle; service.StartSwitching(ordered); service.StartScrolling(scroll with { MayActivate = true });
            var settings = new WindowSwitcherWindow(); settings.Close();
            check(service.IsSwitching && service.IsScrolling, "closing settings leaves both tasks running");
            var previousSwitches = platform.Activations.Count; var previousWheels = platform.Wheels.Count;
            clock.Advance(10); service.Tick();
            check(platform.Activations.Count > previousSwitches && platform.Wheels.Count > previousWheels,
                "switching and scrolling continue executing after the settings window is closed");
            var reopened = new WindowSwitcherWindow();
            try
            {
                var controls = new[] { "_switchRun", "_scrollRun" }.Select(name =>
                    (RunControl)typeof(WindowSwitcherWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reopened)!).ToArray();
                check(controls.Length == 2 && controls.All(control => control.IsRunning), "reopening settings reflects the live running state");
                var toggle = controls[0]; var button = (Button)toggle.Content;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!service.IsSwitching && (string)button.Content == "开始", "same capsule click stops the task");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(service.IsSwitching && (string)button.Content == "停止", "same capsule click starts the task again");
                check(toggle.Content is Button, "run control has one full-size click target");
                render(reopened, "automation-running-v9.png");
                Find<TabControl>((FrameworkElement)reopened.Content).Single().SelectedIndex = 1;
                render(reopened, "content-scrolling-v9.png");
            }
            finally { reopened.Close(); }
            var main = new CatFish.UI.MainWindow();
            using (var tray = new TrayService(main))
            {
                var menu = (Forms.ContextMenuStrip)typeof(TrayService).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
                var automation = (Forms.ToolStripMenuItem)menu.Items["automation"]!;
                var switching = (Forms.ToolStripMenuItem)automation.DropDownItems["switch-tasks"]!;
                var scrolling = (Forms.ToolStripMenuItem)automation.DropDownItems["scroll-tasks"]!;
                check(!switching.DropDownItems[1].Enabled && switching.DropDownItems[2].Enabled, "tray submenu reflects a running switch task");
                switching.DropDownItems[2].PerformClick(); scrolling.DropDownItems[2].PerformClick();
                check(!service.IsSwitching && !service.IsScrolling, "tray stop submenus stop both tasks while settings are closed");
                switching.DropDownItems[1].PerformClick(); scrolling.DropDownItems[1].PerformClick();
                check(service.IsSwitching && service.IsScrolling && !main.IsVisible, "tray start submenus restart both tasks without opening main window");
                service.StopAll();
            }
        }
        finally { service.StopAll(); AppController.Automation = original; }
        RunNativeSwitching(check);
    }

    private static void RunNativeSwitching(Action<bool, string> check)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add("--native-targets");
        using var child = Process.Start(start)!;
        var native = new WindowsAutomationPlatform(); var originalForeground = native.ForegroundWindow;
        WindowAutomationService? service = null;
        try
        {
            var line = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()!;
            var handles = line.Split(':').Select(long.Parse).Select(value => new nint(value)).ToArray();
            var clock = new Clock(); service = new WindowAutomationService(native, clock);
            var targets = handles.Select((handle, index) => new WindowTarget(handle, $"Native test {index}", "test", child.Id)).ToArray();
            check(targets.All(native.IsAvailable), "real cross-process application windows pass selection filtering");
            service.SetSwitchTargets(targets);
            service.StartSwitching(new SwitchOptions(10, 10, "Ordered", true));
            check(native.ForegroundWindow == handles[0], "first cross-process switch actually obtains foreground focus");
            for (var index = 1; index <= 4; index++)
            {
                clock.Advance(10); service.Tick();
                check(native.ForegroundWindow == handles[index % 2], $"cross-process scheduled switch {index} actually obtains foreground focus");
            }
            service.StopSwitching();
            NativeMethods.ShowWindowAsync(handles[1], NativeMethods.SwMinimize);
            check(SpinWait.SpinUntil(() => NativeMethods.IsIconic(handles[1]), 1000), "test target finishes minimizing before restore");
            var activated = native.TryActivate(handles[1]);
            check(activated && !NativeMethods.IsIconic(handles[1]), $"activation restores a minimized application window (activated={activated}, iconic={NativeMethods.IsIconic(handles[1])})");
            check(native.SendWheel(handles[1], -360), "wheel message is delivered to the selected application's focused control");
            var wheel = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            check(wheel == $"wheel:{handles[1].ToInt64()}:-360", "target process receives the exact signed scroll delta");
            check(!native.SendWheel(handles[0], -120), "wheel is never sent to a different inactive target");
        }
        finally
        {
            service?.StopAll();
            if (NativeMethods.IsWindow(originalForeground)) native.TryActivate(originalForeground);
            if (!child.HasExited)
            {
                child.StandardInput.WriteLine("exit"); child.StandardInput.Flush();
                if (!child.WaitForExit(3000)) child.Kill();
            }
        }
    }

    public static void RunNativeTargets()
    {
        using var first = new ProbeWindow { Text = "CatFish test A" };
        using var second = new ProbeWindow { Text = "CatFish test B" };
        first.Shown += (_, _) =>
        {
            second.Show(); Console.WriteLine($"{first.Handle.ToInt64()}:{second.Handle.ToInt64()}");
        };
        _ = Task.Run(() =>
        {
            Console.ReadLine();
            first.BeginInvoke(new Action(() => { second.Close(); first.Close(); }));
        });
        Forms.Application.Run(first);
    }

    private sealed class ProbeWindow : Forms.Form
    {
        public ProbeWindow()
        {
            StartPosition = Forms.FormStartPosition.Manual;
            Location = new System.Drawing.Point(-15000, -15000); Size = new System.Drawing.Size(200, 120);
        }
        protected override bool ShowWithoutActivation => true;
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x020A) Console.WriteLine($"wheel:{Handle.ToInt64()}:{unchecked((short)(message.WParam.ToInt64() >> 16))}");
            base.WndProc(ref message);
        }
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
