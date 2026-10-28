using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CatFish.Core;

namespace CatFish.Features.Transparency;

internal sealed class OpacityToastWindow : Window
{
    private static OpacityToastWindow? _current;
    private readonly TextBlock _value = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private nint _target;
    private DateTime _hideAt;

    internal static OpacityToastWindow? Current => _current;
    internal nint Target => _target;
    internal string DisplayText => _value.Text;

    private OpacityToastWindow()
    {
        Title = "透明度提示 · CatFish";
        Width = 112; Height = 28; Left = -32000; Top = -32000;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Focusable = false; IsHitTestVisible = false; UseLayoutRounding = true;
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        _value.FontSize = 12;
        _value.Foreground = (Brush)Application.Current.Resources["TextBrush"];
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        _value.VerticalAlignment = VerticalAlignment.Center;
        Content = new Border
        {
            Background = (Brush)Application.Current.Resources["CanvasBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrushSoft"], BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Child = _value
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle,
                new nint(style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow | 0x20));
            HwndSource.FromHwnd(handle)?.AddHook(NonInteractiveHook);
        };
        _timer.Tick += (_, _) =>
        {
            if (DateTime.UtcNow >= _hideAt || NativeMethods.GetForegroundWindow() != _target || !PositionOverTarget())
            { _timer.Stop(); Hide(); }
        };
        Closed += (_, _) => { _timer.Stop(); if (ReferenceEquals(_current, this)) _current = null; };
    }

    internal static void ShowFor(nint target, int percent)
    {
        if (!NativeMethods.IsWindow(target) || NativeMethods.IsIconic(target) || !NativeMethods.IsWindowVisible(target))
        { CloseCurrent(); return; }
        var toast = _current ??= new OpacityToastWindow();
        toast._target = target;
        percent = Math.Clamp(percent, 0, 100);
        toast._value.Text = $"透明度 {percent}%";
        toast._hideAt = DateTime.UtcNow.AddSeconds(1);
        new WindowInteropHelper(toast).EnsureHandle();
        if (!toast.PositionOverTarget()) { CloseCurrent(); return; }
        if (!toast.IsVisible) toast.Show();
        toast.PositionOverTarget();
        toast._timer.Stop(); toast._timer.Start();
    }

    private bool PositionOverTarget()
    {
        if (!NativeMethods.IsWindow(_target) || !NativeMethods.IsWindowVisible(_target) || NativeMethods.IsIconic(_target)
            || !NativeMethods.GetWindowRect(_target, out var target)) return false;
        var monitor = NativeMethods.MonitorFromWindow(_target, 2);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        var work = NativeMethods.GetMonitorInfo(monitor, ref info) ? info.Work : target;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bounds = CalculateBounds(new Rect(target.Left, target.Top, Math.Max(0, target.Right - target.Left), Math.Max(0, target.Bottom - target.Top)),
            new Rect(work.Left, work.Top, Math.Max(1, work.Right - work.Left), Math.Max(1, work.Bottom - work.Top)),
            new Size(Width * dpi.DpiScaleX, Height * dpi.DpiScaleY), 6 * dpi.DpiScaleY);
        // GetWindowRect and SetWindowPos use native screen coordinates, including negative monitor origins.
        return NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, NativeMethods.HwndTopmost,
            (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y), (int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), NativeMethods.SwpNoActivate);
    }

    internal static Rect CalculateBounds(Rect target, Rect work, Size size, double gap)
    {
        var width = Math.Min(size.Width, work.Width); var height = Math.Min(size.Height, work.Height);
        var left = Math.Clamp(target.Left + (target.Width - width) / 2, work.Left, work.Right - width);
        // Prefer the space outside the target. At the screen's top edge keep the toast readable.
        var top = Math.Clamp(target.Top - height - gap, work.Top, work.Bottom - height);
        return new Rect(left, top, width, height);
    }

    private static nint NonInteractiveHook(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x84) { handled = true; return new nint(-1); } // HTTRANSPARENT
        if (message == 0x21) { handled = true; return new nint(3); } // MA_NOACTIVATE
        return nint.Zero;
    }

    internal static void CloseCurrent() => _current?.Close();
}
