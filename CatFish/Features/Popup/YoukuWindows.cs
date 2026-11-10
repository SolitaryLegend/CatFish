using CatFish.Core;

namespace CatFish.Features.Popup;

internal static class YoukuWindows
{
    internal static bool IsApplication(WindowTarget target) =>
        target.ProcessName.Equals("YOUKU", StringComparison.OrdinalIgnoreCase);

    internal static nint GetPlaybackOwner(WindowTarget target)
    {
        if (!IsApplication(target)) return nint.Zero;
        var owner = NativeMethods.GetWindow(target.Handle, NativeMethods.GwOwner);
        return IsPlaybackPair(target.Handle, owner, (uint)target.ProcessId) ? owner : nint.Zero;
    }

    internal static nint FindPlaybackSurface(WindowTarget owner)
    {
        if (!IsApplication(owner)) return nint.Zero;
        nint surface = nint.Zero;
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (NativeMethods.GetWindow(handle, NativeMethods.GwOwner) != owner.Handle
                || !IsPlaybackPair(handle, owner.Handle, (uint)owner.ProcessId)) return true;
            surface = handle;
            return false;
        }, nint.Zero);
        return surface;
    }

    private static bool IsPlaybackPair(nint surface, nint owner, uint processId) =>
        owner != nint.Zero && NativeMethods.IsWindowVisible(surface)
        && NativeMethods.GetWindowThreadProcessId(owner, out var ownerPid) != 0 && ownerPid == processId
        && NativeMethods.GetWindowThreadProcessId(surface, out var surfacePid) != 0 && surfacePid == processId
        && (NativeMethods.GetWindowLongPtr(surface, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExLayered) != 0
        && NativeMethods.GetWindowTextLength(surface) > 0
        && NativeMethods.GetWindowRect(owner, out var outer) && NativeMethods.GetWindowRect(surface, out var inner)
        && Math.Abs(outer.Left - inner.Left) <= 8 && Math.Abs(outer.Top - inner.Top) <= 8
        && Math.Abs(outer.Right - inner.Right) <= 8 && Math.Abs(outer.Bottom - inner.Bottom) <= 8;
}
