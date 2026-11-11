using System.Windows.Interop;
using System.Windows;
using CatFish.Core;
using CatFish.Features.Reader;

namespace CatFish.Features.Transparency;

public sealed class WindowAppearanceService
{
    private const int OneKeyOpacity = 40;
    private sealed record OriginalState(nint Style, bool Topmost, bool HadLayered, bool HasLayeredAttributes, byte Alpha, uint ColorKey, uint Flags);
    private readonly Dictionary<nint, OriginalState> _original = new();
    private readonly Dictionary<Window, (double Opacity, bool Topmost)> _wpfOriginal = new();

    // Resolve the foreground HWND on every key press without activating CatFish.
    // A null change toggles between 40% and full opacity; other values are percentage-point steps.
    public bool TryChangeForegroundOpacity(int? change, out string error)
        => TryChangeForegroundOpacity(change, out _, out _, out error);

    public bool TryChangeForegroundOpacity(int? change, out nint handle, out int percent, out string error)
    {
        handle = NativeMethods.GetForegroundWindow();
        percent = 100;
        if (handle == nint.Zero || !NativeMethods.IsWindow(handle))
        { error = "当前没有可调整透明度的前台窗口。"; return false; }

        if (HwndSource.FromHwnd(handle)?.RootVisual is Window { AllowsTransparency: true } ownWindow)
        {
            percent = NextOpacityPercent(ownWindow.Opacity, change);
            return TrySetOpacity(ownWindow, percent / 100d, out error);
        }

        var current = 1d;
        if (NativeMethods.GetLayeredWindowAttributes(handle, out _, out var alpha, out var flags)
            && (flags & NativeMethods.LwaAlpha) != 0)
            current = alpha / 255d;
        percent = NextOpacityPercent(current, change);
        return TrySetOpacity(handle, percent / 100d, out error);
    }

    private static int NextOpacityPercent(double current, int? change)
    {
        if (change is int step)
            return (int)Math.Clamp(Math.Round(current * 100) + step, 0, 100);
        return current < 1 ? 100 : OneKeyOpacity;
    }

    public bool TrySetOpacity(Window window, double opacity, out string error)
    {
        if (window is TxtReaderWindow) { error = "请在单行阅读设置中单独调整文字和底板透明度。"; return false; }
        if (!window.IsLoaded) { error = "CatFish 主窗口尚未加载。"; return false; }
        if (!_wpfOriginal.ContainsKey(window)) _wpfOriginal[window] = (window.Opacity, window.Topmost);
        // Opacity needs a per-pixel-alpha HWND; otherwise WPF composites against black.
        // The launcher enables AllowsTransparency before its HWND is created.
        if (!window.AllowsTransparency) { error = "此 CatFish 窗口未启用桌面透明合成。"; return false; }
        window.Opacity = Math.Clamp(opacity, 0, 1); error = string.Empty; return true;
    }
    private void Capture(nint h)
    {
        if (_original.ContainsKey(h)) return;
        var style = NativeMethods.GetWindowLongPtr(h, NativeMethods.GwlExStyle); var layered = (style.ToInt64() & NativeMethods.WsExLayered) != 0;
        byte alpha = 255; uint color = 0, flags = 0; var hasAttributes = layered && NativeMethods.GetLayeredWindowAttributes(h, out color, out alpha, out flags);
        _original[h] = new(style, (style.ToInt64() & NativeMethods.WsExTopmost) != 0, layered, hasAttributes, alpha, color, flags);
    }
    public bool TrySetOpacity(nint h, double opacity, out string error)
    {
        if (h == nint.Zero || !NativeMethods.IsWindow(h)) { error = "此窗口已关闭，请切换到目标窗口后重试。"; return false; }
        if (HwndSource.FromHwnd(h)?.RootVisual is TxtReaderWindow) { error = "请在单行阅读设置中单独调整文字和底板透明度。"; return false; }
        var style = NativeMethods.GetWindowLongPtr(h, NativeMethods.GwlExStyle); Capture(h);
        uint colorKey = 0, flags = NativeMethods.LwaAlpha;
        if (NativeMethods.GetLayeredWindowAttributes(h, out var existingColor, out _, out var existingFlags))
        { colorKey = existingColor; flags |= existingFlags & NativeMethods.LwaColorKey; }
        if ((style.ToInt64() & NativeMethods.WsExLayered) == 0) NativeMethods.SetWindowLongPtr(h, NativeMethods.GwlExStyle, new nint(style.ToInt64() | NativeMethods.WsExLayered));
        var alpha = (byte)Math.Clamp((int)Math.Round(Math.Clamp(opacity, 0, 1) * 255), 0, 255);
        if (!NativeMethods.SetLayeredWindowAttributes(h, colorKey, alpha, flags)) { error = "Windows 未能更新该窗口。它可能受保护或以更高权限运行。"; Restore(h); return false; }
        error = string.Empty; return true;
    }
    public void Restore(nint h)
    {
        var ownWindow = _wpfOriginal.Keys.FirstOrDefault(w => new WindowInteropHelper(w).Handle == h);
        if (ownWindow is not null && _wpfOriginal.Remove(ownWindow, out var ownState))
        { ownWindow.Opacity = ownState.Opacity; ownWindow.Topmost = ownState.Topmost; }
        if (!_original.Remove(h, out var state) || !NativeMethods.IsWindow(h)) return;
        NativeMethods.SetWindowLongPtr(h, NativeMethods.GwlExStyle, state.Style);
        NativeMethods.SetWindowPos(h, state.Topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNoTopmost, 0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        if (state.HadLayered)
            NativeMethods.SetLayeredWindowAttributes(h, state.ColorKey, state.Alpha, state.HasLayeredAttributes ? state.Flags : NativeMethods.LwaAlpha);
    }
    public void Restore(Window window)
    {
        Restore(new WindowInteropHelper(window).Handle);
    }
    public void RestoreAll()
    {
        foreach (var window in _wpfOriginal.Keys.ToArray()) Restore(window);
        foreach (var h in _original.Keys.ToArray()) Restore(h);
    }
}
