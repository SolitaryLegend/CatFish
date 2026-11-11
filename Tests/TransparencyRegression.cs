using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CatFish.Core;
using CatFish.Features.Automation;
using CatFish.Features.Reader;
using CatFish.Features.Transparency;
using Forms = System.Windows.Forms;

internal static class TransparencyRegression
{
    public static void Run(Action<bool, string> check, Action<Window, string> render)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var centered = OpacityToastWindow.CalculateBounds(new Rect(100, 80, 500, 300), new Rect(0, 0, 1920, 1040), new Size(112, 28), 6);
        check(centered == new Rect(294, 46, 112, 28), "compact toast centers outside the target's top edge");
        var negative = OpacityToastWindow.CalculateBounds(new Rect(-1900, 50, 600, 400), new Rect(-1920, 0, 1920, 1040), new Size(112, 28), 6);
        check(negative.Left == -1656 && negative.Top == 16, "toast positioning preserves negative monitor coordinates");
        var maximized = OpacityToastWindow.CalculateBounds(new Rect(-8, -8, 1936, 1096), new Rect(0, 0, 1920, 1040), new Size(168, 42), 9);
        check(maximized.Top == 0 && maximized.Left >= 0 && maximized.Right <= 1920, "maximized targets keep the toast visible at the screen edge when there is no external space");
        var edge = OpacityToastWindow.CalculateBounds(new Rect(1900, 1100, 300, 200), new Rect(0, 0, 1920, 1040), new Size(112, 28), 6);
        check(edge.Right == 1920 && edge.Bottom == 1040, "partially offscreen targets keep the toast visible");

        var originalForeground = NativeMethods.GetForegroundWindow();
        var platform = new WindowsAutomationPlatform();
        var target = new Window
        {
            Title = "CatFish transparency test", Width = 520, Height = 260, Left = 160, Top = 120,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Content = new Border { Background = new SolidColorBrush(Color.FromRgb(222, 236, 228)) }
        };
        using var native = new Forms.Form
        {
            Text = "CatFish native transparency test", StartPosition = Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(220, 180), Size = new System.Drawing.Size(500, 260)
        };
        try
        {
            target.Show(); Pump(70);
            var handle = new WindowInteropHelper(target).Handle;
            check(platform.TryActivate(handle), "test target receives foreground focus");
            Hotkey("DecreaseOpacity");
            var toast = OpacityToastWindow.Current!;
            check(toast.Width == 112 && toast.Height == 28 && toast.Content is Border { Child: TextBlock, CornerRadius.TopLeft: 14 },
                "toast is a compact capsule containing the opacity label and percentage, without a progress bar");
            check(toast is { IsVisible: true } && toast.Target == handle && toast.DisplayText == "透明度 90%" && target.Opacity == .9,
                "opacity hotkey displays the successful WPF target percentage");
            var toastHandle = new WindowInteropHelper(toast).Handle;
            var style = NativeMethods.GetWindowLongPtr(toastHandle, NativeMethods.GwlExStyle).ToInt64();
            check(!toast.ShowActivated && !toast.ShowInTaskbar && !toast.Focusable && !toast.IsHitTestVisible
                && (style & (NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow | 0x20)) == (NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow | 0x20),
                "toast is non-activating, absent from taskbar, and mouse-transparent");
            check(NativeMethods.GetForegroundWindow() == handle && !WindowDiscoveryService.IsUserWindow(toastHandle),
                "toast leaves target focus intact and does not appear in application choices");
            NativeMethods.SendMessageTimeout(toastHandle, 0x84, nint.Zero, nint.Zero, 2, 200, out var hit);
            check(hit.ToInt64() == -1, "toast native hit testing allows clicks through");
            NativeMethods.GetWindowRect(handle, out var targetBounds); NativeMethods.GetWindowRect(toastHandle, out var toastBounds);
            check(Math.Abs((toastBounds.Left + toastBounds.Right) / 2 - (targetBounds.Left + targetBounds.Right) / 2) <= 2
                && toastBounds.Bottom <= targetBounds.Top
                && Math.Abs(targetBounds.Top - toastBounds.Bottom - 6 * VisualTreeHelper.GetDpi(toast).DpiScaleY) <= 2,
                "visible toast is outside the actual target window's top center with a small gap");
            Hotkey("DecreaseOpacity"); Hotkey("DecreaseOpacity");
            check(ReferenceEquals(toast, OpacityToastWindow.Current) && toast.DisplayText == "透明度 70%" && target.Opacity == .7,
                "rapid adjustments reuse one toast and show the latest value");
            render(toast, "opacity-toast-labeled-70.png");
            for (var i = 0; i < 8; i++) Hotkey("DecreaseOpacity");
            check(target.Opacity == 0 && toast.DisplayText == "透明度 0%" && toast.Opacity == 1,
                "toast remains readable when its target reaches zero opacity");
            Hotkey("ResetOpacity");
            check(target.Opacity == 1 && toast.DisplayText == "透明度 100%", "toggle restores a fully transparent WPF window to 100 percent");
            Hotkey("ResetOpacity");
            check(target.Opacity == .4 && toast.DisplayText == "透明度 40%", "toggle sets an opaque WPF window to 40 percent");
            Hotkey("ResetOpacity");
            Hotkey("IncreaseOpacity");
            check(toast.DisplayText == "透明度 100%", "upper opacity boundary still produces feedback");

            Pump(650); Hotkey("DecreaseOpacity"); Pump(650);
            check(toast.IsVisible && toast.DisplayText == "透明度 90%", "new adjustments restart the dismissal timer");
            Pump(400);
            check(!toast.IsVisible && NativeMethods.GetForegroundWindow() == handle, "toast auto-hides about one second after the last adjustment without changing focus");

            NativeMethods.ShowWindow(native.Handle, 4); Pump(50);
            check(platform.TryActivate(native.Handle), "native test target receives focus");
            Hotkey("DecreaseOpacity");
            check(ReferenceEquals(toast, OpacityToastWindow.Current) && toast.Target == native.Handle && toast.DisplayText == "透明度 90%"
                && NativeMethods.GetLayeredWindowAttributes(native.Handle, out _, out var alpha, out _) && alpha == 230,
                "same toast moves to a different foreground native window and reports its actual opacity");
            check(NativeMethods.GetForegroundWindow() == native.Handle, "native target retains focus after the toast appears");
            Hotkey("ResetOpacity");
            check(NativeMethods.GetLayeredWindowAttributes(native.Handle, out _, out alpha, out _) && alpha == 255,
                "toggle restores a partially transparent native window to 100 percent");
            Hotkey("ResetOpacity");
            check(NativeMethods.GetLayeredWindowAttributes(native.Handle, out _, out alpha, out _) && alpha == 102,
                "toggle sets an opaque native window to 40 percent");
            check(NativeMethods.SetLayeredWindowAttributes(native.Handle, 0, 254, NativeMethods.LwaAlpha), "test can set native opacity just below 100 percent");
            Hotkey("ResetOpacity");
            check(NativeMethods.GetLayeredWindowAttributes(native.Handle, out _, out alpha, out _) && alpha == 255,
                "toggle restores native opacity even when the displayed percentage would round to 100");
            platform.TryActivate(handle); Pump(100);
            check(!toast.IsVisible, "switching foreground windows dismisses the old target's toast");
            Hotkey("DecreaseOpacity");
            NativeMethods.SetWindowPos(handle, nint.Zero, 340, 260, 0, 0, NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | 4); Pump(100);
            NativeMethods.GetWindowRect(handle, out targetBounds); NativeMethods.GetWindowRect(toastHandle, out toastBounds);
            check(Math.Abs((toastBounds.Left + toastBounds.Right) / 2 - (targetBounds.Left + targetBounds.Right) / 2) <= 2,
                "visible toast follows a moved target window");
            OpacityToastWindow.ShowFor(nint.Zero, 50);
            check(OpacityToastWindow.Current is null, "invalid target never leaves a success toast onscreen");

            using var reader = new TxtReaderService(new TxtReaderSettings(), () => Task.CompletedTask);
            var readerWindow = new TxtReaderWindow(reader);
            check(!AppController.WindowAppearance.TrySetOpacity(readerWindow, .5, out _) && OpacityToastWindow.Current is null,
                "protected reader keeps its independent appearance and does not display success feedback");
            readerWindow.Close();
            Hotkey("ResetOpacity");
            AppController.EmergencyStop();
            check(OpacityToastWindow.Current is null, "emergency stop closes the toast");
        }
        finally
        {
            OpacityToastWindow.CloseCurrent(); AppController.WindowAppearance.RestoreAll();
            target.Close();
            if (NativeMethods.IsWindow(originalForeground)) platform.TryActivate(originalForeground);
        }
    }

    private static void Hotkey(string action) => typeof(AppController).GetMethod("HandleHotkey", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { action });

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
