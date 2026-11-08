using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CatFish.Core;
using CatFish.Features.Automation;
using CatFish.Features.Popup;
using CatFish.Features.Reader;
using CatFish.UI;

internal static class EmergencyStopRegression
{
    public static void Run(Action<bool, string> check)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var path = Path.Combine(AppContext.BaseDirectory, "emergency-reader.txt");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("紧急停止保留阅读进度，并关闭全部窗口。", 100)));
        var automation = AppController.Automation;
        AppController.Automation = new(new Platform());
        var main = new MainWindow();
        var events = new List<string>();
        var cancelled = new Window { Title = "Unsaved child", Width = 240, Height = 180 };
        var cancelClose = true;
        cancelled.Closing += (_, args) => args.Cancel = cancelClose;
        try
        {
            main.Show();
            var reader = AppController.TxtReader;
            Wait(reader.OpenAsync(path)); reader.SeekPercent(25);
            AppController.ShowTxtReader(); reader.Play();
            var floatingReader = AppController.ReaderWindow!;
            floatingReader.Closed += (_, _) => events.Add("reader");
            var target = new WindowTarget(new nint(1), "Test", "Test", 1);
            AppController.Automation.SetSwitchTargets([target]);
            AppController.Automation.SetScrollTarget(target);
            AppController.StartSwitching(); AppController.StartScrolling();

            var popup = new AdvertisementPopupWindow(AppController.Settings); popup.Show();
            popup.Closed += (_, _) => events.Add("popup");
            var popupSettings = new PopupSettingsWindow(); popupSettings.Show();
            var schedule = (DispatcherTimer)typeof(PopupSettingsWindow).GetField("_scheduleTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(popupSettings)!;
            schedule.Interval = TimeSpan.FromHours(1); schedule.Start();

            var child = new Window { Width = 250, Height = 180 };
            AppController.ShowFeature("emergency-probe", () => child);
            var nested = new Window { Owner = child, Width = 220, Height = 150 }; nested.Show();
            var independent = new Window { Width = 230, Height = 160 }; independent.Show();
            cancelled.Show();
            foreach (var window in new Window[] { popupSettings, child, nested, independent, cancelled })
                window.Closing += (_, _) => check(!reader.IsPlaying && AppController.ReaderWindow is null && !popup.IsVisible
                    && !schedule.IsEnabled && !AppController.Automation.IsSwitching && !AppController.Automation.IsScrolling,
                    "feature activity and floating output stop before a child window closes");
            nested.Closed += (_, _) => events.Add("nested");
            child.Closed += (_, _) => events.Add("child");
            independent.Closed += (_, _) => events.Add("independent");
            var mainClosingCount = 0;
            main.Closing += (_, _) =>
            {
                mainClosingCount++; events.Add("main");
                check(!popupSettings.IsVisible && !child.IsVisible && !nested.IsVisible && !independent.IsVisible && !cancelled.IsVisible,
                    "main window closes only after every child window is gone or hidden with its unsaved edits");
            };

            typeof(AppController).GetMethod("HandleHotkey", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, ["EmergencyStop"]);
            check(!main.IsVisible && mainClosingCount == 1 && !main.ExitRequested, "emergency hotkey closes the launcher to tray last without exiting shortcut services");
            check(events.IndexOf("nested") < events.IndexOf("child") && events.Last() == "main",
                "nested dialogs close before their parent and the main window closes last");
            check(reader.Offset > 0 && !reader.IsPlaying && AppController.ReaderWindow is null,
                "reader floating window closes while its reading position survives");
            check(!Application.Current.Windows.Cast<Window>().Any(window => window.IsVisible), "emergency stop leaves no application window visible");
            var features = (Dictionary<string, Window>)typeof(AppController).GetField("Features", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            check(!features.ContainsKey("emergency-probe"), "closed feature windows are removed from the registry");
            AppController.EmergencyStop();
            check(!main.IsVisible && AppController.ReaderWindow is null, "repeated emergency stop remains safe and does not reopen any window");
            cancelClose = false; cancelled.Close();
            AppController.ReturnToMain(); AppController.ShowTxtReader();
            check(main.IsVisible && AppController.ReaderWindow is { IsVisible: true }, "main and reader can be reopened after emergency stop");
            AppController.EmergencyStop();

            var reachedSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var pendingReader = new TxtReaderService(new(), () => { reachedSave.TrySetResult(); return releaseSave.Task; });
            var pendingOpen = pendingReader.OpenAsync(path);
            Wait(reachedSave.Task); pendingReader.EmergencyStop(); releaseSave.SetResult(); Wait(pendingOpen);
            check(!pendingOpen.Result && !pendingReader.IsPlaying && !pendingReader.IsLoading,
                "a reader load finishing after emergency stop cannot report success and reopen its floating window");
        }
        finally
        {
            cancelClose = false; cancelled.Close();
            main.ExitRequested = true; main.Close();
            AppController.Automation = automation;
            File.Delete(path);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void Wait(Task task)
    {
        while (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private sealed class Platform : IWindowAutomationPlatform
    {
        public nint ForegroundWindow { get; private set; }
        public bool IsAvailable(WindowTarget target) => true;
        public bool TryActivate(nint handle) { ForegroundWindow = handle; return true; }
        public bool SendWheel(nint target, int delta) => true;
    }
}
