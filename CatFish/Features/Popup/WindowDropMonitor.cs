using System.Windows.Threading;
using CatFish.Core;

namespace CatFish.Features.Popup;

public sealed class WindowDropMonitor : IDisposable
{
    private const uint MoveSizeEnd = 0x000B, SkipOwnProcess = 2, RootWindow = 2;
    private readonly Action<nint, NativeMethods.NativePoint> _dropped;
    private readonly NativeMethods.WinEventProc _callback;
    private readonly DispatcherTimer _mouseTimer;
    private readonly int _dragButton = System.Windows.Forms.SystemInformation.MouseButtonsSwapped ? 2 : 1;
    private nint _hook;
    private bool _buttonDown;
    private DragStart? _dragStart;

    public WindowDropMonitor(Action<nint, NativeMethods.NativePoint> dropped)
    {
        _dropped = dropped;
        _callback = (_, _, handle, objectId, childId, _, _) =>
        {
            if (objectId != 0 || childId != 0) return;
            var root = NativeMethods.GetAncestor(handle, RootWindow);
            if (!TryGetExternalProcess(root, out _) || !NativeMethods.GetCursorPos(out var point)) return;
            if (_dragStart?.Handle == root) _dragStart = null;
            _dropped(root, point);
        };
        _hook = NativeMethods.SetWinEventHook(MoveSizeEnd, MoveSizeEnd, nint.Zero, _callback, 0, 0, SkipOwnProcess);

        // Custom title bars can move their HWND without entering the Windows move/size loop.
        _buttonDown = IsDragButtonDown();
        _mouseTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(20) };
        _mouseTimer.Tick += TrackMouseDrag;
        _mouseTimer.Start();
    }

    private bool IsDragButtonDown() => NativeMethods.GetAsyncKeyState(_dragButton) < 0;

    private void TrackMouseDrag(object? sender, EventArgs e)
    {
        var buttonDown = IsDragButtonDown();
        if (buttonDown == _buttonDown) return;
        _buttonDown = buttonDown;
        var start = _dragStart;
        _dragStart = null;
        if (!NativeMethods.GetCursorPos(out var point)) return;

        if (buttonDown)
        {
            var root = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), RootWindow);
            if (TryGetExternalProcess(root, out var processId) && NativeMethods.GetWindowRect(root, out var bounds))
                _dragStart = new(root, processId, point, bounds);
            return;
        }

        if (start is not { } drag || !TryGetExternalProcess(drag.Handle, out var currentProcessId)
            || currentProcessId != drag.ProcessId || !NativeMethods.GetWindowRect(drag.Handle, out var end)) return;
        var dx = end.Left - drag.Bounds.Left;
        var dy = end.Top - drag.Bounds.Top;
        // Require a translation that follows the pointer, so clicks, content drags and resizes do not embed.
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < 8
            || end.Right - drag.Bounds.Right != dx || end.Bottom - drag.Bounds.Bottom != dy
            || Math.Abs(point.X - drag.Point.X - dx) > 24 || Math.Abs(point.Y - drag.Point.Y - dy) > 24) return;
        _dropped(drag.Handle, point);
    }

    private static bool TryGetExternalProcess(nint handle, out uint processId) =>
        NativeMethods.GetWindowThreadProcessId(handle, out processId) != 0 && processId != Environment.ProcessId;

    public void Dispose()
    {
        _mouseTimer.Stop();
        if (_hook != nint.Zero) NativeMethods.UnhookWinEvent(_hook);
        _hook = nint.Zero; GC.KeepAlive(_callback);
    }

    private readonly record struct DragStart(nint Handle, uint ProcessId, NativeMethods.NativePoint Point, NativeMethods.Rect Bounds);
}
