using System.Runtime.InteropServices;
using CatFish.Core;

namespace CatFish.Features.Automation;

public sealed class WindowsAutomationPlatform : IWindowAutomationPlatform
{
    public nint ForegroundWindow => NativeMethods.GetForegroundWindow();
    public bool IsAvailable(WindowTarget target)
    {
        if (!WindowDiscoveryService.IsUserWindow(target.Handle)) return false;
        NativeMethods.GetWindowThreadProcessId(target.Handle, out var pid);
        return pid == (uint)target.ProcessId;
    }

    public bool TryActivate(nint handle)
    {
        if (!NativeMethods.IsWindow(handle) || NativeMethods.IsHungAppWindow(handle)) return false;
        if (NativeMethods.IsIconic(handle))
        {
            if (!NativeMethods.ShowWindowAsync(handle, NativeMethods.SwRestore)
                || !SpinWait.SpinUntil(() => !NativeMethods.IsIconic(handle) || !NativeMethods.IsWindow(handle), 300)) return false;
            if (!NativeMethods.IsWindow(handle)) return false;
        }
        NativeMethods.SetForegroundWindow(handle);
        FlushWindow(handle);
        if (ForegroundWindow == handle && !NativeMethods.IsIconic(handle)) return true;

        var currentThread = NativeMethods.GetCurrentThreadId();
        var foregroundThread = NativeMethods.IsHungAppWindow(ForegroundWindow) ? 0 : NativeMethods.GetWindowThreadProcessId(ForegroundWindow, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(handle, out _);
        var attached = new List<uint>();
        try
        {
            foreach (var thread in new[] { foregroundThread, targetThread }.Distinct())
                if (thread != 0 && thread != currentThread && NativeMethods.AttachThreadInput(currentThread, thread, true)) attached.Add(thread);
            if (NativeMethods.IsIconic(handle)) NativeMethods.ShowWindowAsync(handle, NativeMethods.SwRestore);
            NativeMethods.BringWindowToTop(handle);
            NativeMethods.SetForegroundWindow(handle);
        }
        finally
        {
            foreach (var thread in attached.AsEnumerable().Reverse()) NativeMethods.AttachThreadInput(currentThread, thread, false);
        }
        FlushWindow(handle);
        return ForegroundWindow == handle && !NativeMethods.IsIconic(handle);
    }

    private static void FlushWindow(nint handle) => NativeMethods.SendMessageTimeout(handle, 0, nint.Zero, nint.Zero, 2, 200, out _);

    public bool SendWheel(nint target, int delta)
    {
        if (ForegroundWindow != target || !NativeMethods.IsWindow(target)) return false;
        var thread = NativeMethods.GetWindowThreadProcessId(target, out _);
        var info = new NativeMethods.GuiThreadInfo { Size = Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        var focus = target;
        if (NativeMethods.GetGUIThreadInfo(thread, ref info) && info.Focus != nint.Zero
            && (info.Focus == target || NativeMethods.IsChild(target, info.Focus))) focus = info.Focus;
        if (!NativeMethods.GetWindowRect(focus, out var bounds)) return false;
        var x = (bounds.Left + bounds.Right) / 2; var y = (bounds.Top + bounds.Bottom) / 2;
        var point = new nint(unchecked((int)((uint)(ushort)x | ((uint)(ushort)y << 16))));
        var wheel = new nint(unchecked((int)((uint)(ushort)delta << 16)));
        // Deliver to the chosen window's focus control, independent of mouse position.
        return NativeMethods.SendMessageTimeout(focus, 0x020A, wheel, point, 2, 200, out _) != nint.Zero;
    }
}
