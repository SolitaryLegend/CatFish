using CatFish.Core;

namespace CatFish.Features.Popup;

public sealed class WindowDropMonitor : IDisposable
{
    private readonly NativeMethods.WinEventProc _callback;
    private nint _hook;
    public WindowDropMonitor(Action<nint, NativeMethods.NativePoint> dropped)
    {
        _callback = (_, _, handle, _, _, _, _) =>
        {
            if (handle == nint.Zero || NativeMethods.GetWindowThreadProcessId(handle, out var processId) == 0
                || processId == Environment.ProcessId || !NativeMethods.GetCursorPos(out var point)) return;
            dropped(handle, point);
        };
        _hook = NativeMethods.SetWinEventHook(0xB, 0xB, nint.Zero, _callback, 0, 0, 2);
    }
    public void Dispose()
    {
        if (_hook != nint.Zero) NativeMethods.UnhookWinEvent(_hook);
        _hook = nint.Zero; GC.KeepAlive(_callback);
    }
}
