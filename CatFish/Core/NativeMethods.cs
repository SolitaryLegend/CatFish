using System.Runtime.InteropServices;
using System.Text;

namespace CatFish.Core;

public static class NativeMethods
{
    public const int GwlExStyle = -20; public const long WsExLayered = 0x00080000L, WsExTopmost = 0x00000008L;
    public const int GwlStyle = -16, DwmwaCloaked = 14;
    public const long WsChild = 0x40000000L, WsExToolWindow = 0x00000080L, WsExNoActivate = 0x08000000L;
    public const uint LwaAlpha = 2, LwaColorKey = 1, GwOwner = 4, SwMinimize = 6, SwRestore = 9;
    public const uint SwpNoSize = 1, SwpNoMove = 2, SwpNoActivate = 0x10;
    public const uint SwpFrameChanged = 0x20, SwpNoCopyBits = 0x100, SwpAsyncWindowPos = 0x4000;
    public const uint RdwInvalidate = 0x1, RdwErase = 0x4, RdwAllChildren = 0x80, RdwFrame = 0x400;
    public const long WsPopup = 0x80000000L, WsCaption = 0x00C00000L, WsThickFrame = 0x00040000L;
    public const long WsMinimize = 0x20000000L, WsMaximize = 0x01000000L;
    public const long WsVisible = 0x10000000L, WsClipChildren = 0x02000000L, WsClipSiblings = 0x04000000L;
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] public static extern nint GetParent(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint handle);
    [DllImport("user32.dll")] public static extern bool GetClientRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(nint handle, ref WindowPlacement placement);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(nint handle, ref WindowPlacement placement);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out NativePoint point);
    public delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);
    [StructLayout(LayoutKind.Sequential)] public struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct WindowPlacement
    {
        public int Length; public uint Flags, ShowCommand; public NativePoint MinPosition, MaxPosition; public Rect NormalPosition;
    }
    public static readonly nint HwndTopmost = new(-1), HwndNoTopmost = new(-2);
    public const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModNoRepeat = 0x4000; public const int WmHotkey = 0x312;
    public const int WheelDelta = 120;
    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] public static extern nint GetShellWindow();
    [DllImport("user32.dll")] public static extern nint GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hWnd, out Rect rect);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hWnd, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hWnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hWnd, uint command);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] public static extern nint GetWindowLongPtr(nint hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetLayeredWindowAttributes(nint hWnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(nint hWnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(nint hWnd, nint updateRect, nint updateRegion, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hWnd, uint command);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(nint hWnd, uint command);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(nint hWnd);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint sourceThread, uint targetThread, bool attach);
    [DllImport("user32.dll")] public static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint SendMessageTimeout(nint hWnd, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] public struct GuiThreadInfo
    {
        public int Size; public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(nint hWnd);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }
    public delegate bool MonitorEnumProc(nint monitor, nint dc, nint rect, nint data);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
