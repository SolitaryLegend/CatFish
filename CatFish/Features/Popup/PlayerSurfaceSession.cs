using CatFish.Core;

namespace CatFish.Features.Popup;

// The video window is embedded separately. The player's owned surface retains its native composition.
internal sealed class PlayerSurfaceSession
{
    private const uint OrderFlags = NativeMethods.SwpNoMove | NativeMethods.SwpNoSize
        | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder;
    // Youku's minimum window width can exceed the host width.
    private const uint PositionFlags = NativeMethods.SwpNoActivate
        | NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpNoSendChanging;

    private readonly nint _owner;
    private readonly uint _processId;
    private readonly NativeMethods.Rect? _desktopBounds;
    private nint _handle;
    private bool _wasTopmost;
    private long _nextSearch;

    private PlayerSurfaceSession(WindowTarget owner, nint handle = default, NativeMethods.Rect? desktopBounds = null)
    {
        _owner = owner.Handle;
        _processId = (uint)owner.ProcessId;
        _desktopBounds = desktopBounds;
        _handle = handle;
        _wasTopmost = handle != nint.Zero && IsTopmost(handle);
    }

    internal static PlayerSurfaceSession? Capture(WindowTarget target)
    {
        if (TencentVideoWindows.IsApplication(target) && !TencentVideoWindows.IsSurface(target))
            return new(target);

        var surface = YoukuWindows.FindPlaybackSurface(target);
        return surface != nint.Zero && NativeMethods.GetWindowRect(surface, out var bounds)
            ? new(target, surface, bounds) : null;
    }

    internal void Update(nint popup, bool showAbove)
    {
        if (_desktopBounds is not null)
        {
            if (!HasOwnedSurface || !AlignWithOwner()) return;
        }
        else if (!HasOwnedSurface && !RefreshTencentSurface()) return;

        // The ad mask and modal picker sit above the player surface in the window order.
        var insertAfter = showAbove && FitsOwner() ? NativeMethods.GetWindow(popup, NativeMethods.GwPrevious) : popup;
        if (insertAfter == _handle || (insertAfter != nint.Zero
            && NativeMethods.GetWindow(_handle, NativeMethods.GwPrevious) == insertAfter)) return;
        NativeMethods.SetWindowPos(_handle, insertAfter == nint.Zero ? NativeMethods.HwndTopmost : insertAfter,
            0, 0, 0, 0, OrderFlags);
    }

    internal bool Restore()
    {
        if (IsAlive)
        {
            if (_desktopBounds is { } bounds)
            {
                if (!Move(bounds, _wasTopmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNoTopmost)) return false;
            }
            else if (!_wasTopmost && IsTopmost(_handle)
                && !NativeMethods.SetWindowPos(_handle, NativeMethods.HwndNoTopmost, 0, 0, 0, 0, OrderFlags)) return false;
        }
        _handle = nint.Zero;
        return true;
    }

    private bool RefreshTencentSurface()
    {
        var now = Environment.TickCount64;
        if (now < _nextSearch) return false;
        _nextSearch = now + 1000;
        if (!Restore()) return false;

        _handle = TencentVideoWindows.FindSurface(_owner, _processId);
        if (_handle == nint.Zero) return false;
        _wasTopmost = IsTopmost(_handle);
        return true;
    }

    private bool AlignWithOwner()
    {
        if (!NativeMethods.GetWindowRect(_owner, out var player)
            || !NativeMethods.GetWindowRect(_handle, out var surface)) return false;
        if (player.Left == surface.Left && player.Top == surface.Top
            && player.Right == surface.Right && player.Bottom == surface.Bottom) return true;
        return Move(player, nint.Zero, NativeMethods.SwpNoZOrder);
    }

    private bool Move(NativeMethods.Rect bounds, nint insertAfter, uint flags = 0) =>
        NativeMethods.SetWindowPos(_handle, insertAfter, bounds.Left, bounds.Top,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, PositionFlags | flags);

    private bool FitsOwner() =>
        NativeMethods.GetWindowRect(_owner, out var player) && NativeMethods.GetWindowRect(_handle, out var surface)
        && surface.Left >= player.Left && surface.Top >= player.Top
        && surface.Right <= player.Right && surface.Bottom <= player.Bottom;

    private bool IsAlive => _handle != nint.Zero
        && NativeMethods.GetWindowThreadProcessId(_handle, out var pid) != 0 && pid == _processId;

    private bool HasOwnedSurface => IsAlive && NativeMethods.IsWindowVisible(_handle)
        && NativeMethods.GetWindow(_handle, NativeMethods.GwOwner) == _owner;

    private static bool IsTopmost(nint handle) =>
        (NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTopmost) != 0;
}
