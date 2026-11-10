using System.Runtime.InteropServices;
using CatFish.Core;

namespace CatFish.Features.Popup;

public sealed class EmbeddedWindowSession
{
    private const long SystemMenu = 0x00080000L, MinimizeBox = 0x00020000L, MaximizeBox = 0x00010000L;
    private const long WindowFrameStyles = NativeMethods.WsPopup | NativeMethods.WsCaption | NativeMethods.WsThickFrame
        | NativeMethods.WsMinimize | NativeMethods.WsMaximize | SystemMenu | MinimizeBox | MaximizeBox;
    private const uint RestoreFrameFlags = NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged | NativeMethods.SwpNoCopyBits;

    private nint _handle, _host, _owner, _style, _extendedStyle;
    private uint _processId;
    private NativeMethods.WindowPlacement _placement;
    private NativeMethods.Rect _desktopBounds;
    private bool _restoring;
    private PlayerSurfaceSession? _playerSurface;
    public nint Handle => IsAlive ? _handle : nint.Zero;
    public bool IsAlive => _handle != nint.Zero && NativeMethods.IsWindow(_handle)
        && NativeMethods.GetWindowThreadProcessId(_handle, out var processId) != 0 && processId == _processId;
    internal bool IsAttached => IsAlive && NativeMethods.IsChild(_host, _handle);
    internal bool IsVisibleInHost => IsAttached && HasVisibleStyle;
    // HwndHost can show its native ancestor on a later dispatcher pass.
    private bool HasVisibleStyle =>
        (NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlStyle).ToInt64() & NativeMethods.WsVisible) != 0
        && !NativeMethods.IsIconic(_handle);

    public bool Attach(WindowTarget target, nint host, out string error)
    {
        error = string.Empty;
        if (!NativeMethods.IsWindow(host) || !NativeMethods.IsWindow(target.Handle)
            || NativeMethods.GetWindowThreadProcessId(target.Handle, out var processId) == 0
            || processId != target.ProcessId || (NativeMethods.GetWindowLongPtr(target.Handle, NativeMethods.GwlStyle).ToInt64() & NativeMethods.WsChild) != 0)
        { error = "目标窗口已关闭或已嵌入其他页面，请刷新列表。"; return false; }
        if (!Detach(out error)) return false;
        _handle = target.Handle; _host = host; _processId = processId; _restoring = false;
        _style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlStyle);
        _extendedStyle = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        _owner = NativeMethods.GetWindow(_handle, NativeMethods.GwOwner);
        _placement = new() { Length = Marshal.SizeOf<NativeMethods.WindowPlacement>() };
        if (!NativeMethods.GetWindowPlacement(_handle, ref _placement))
        { _handle = nint.Zero; error = "无法读取窗口位置，未更改目标窗口。"; return false; }
        var restoredBeforeAttach = (_style.ToInt64() & (NativeMethods.WsMinimize | NativeMethods.WsMaximize)) != 0;
        if (restoredBeforeAttach)
        {
            // Check write access before restoring a minimized or maximized foreign window.
            var accessError = SetWindowStyle(_style);
            if (accessError != 0)
            { _handle = nint.Zero; error = EmbedError(accessError); return false; }
            if (NativeMethods.SendMessageTimeout(_handle, 0x112, new nint(0xF120), nint.Zero, 2, 500, out _) == nint.Zero)
            { _handle = nint.Zero; error = "目标窗口未响应还原操作，请先手动还原窗口后重试。"; return false; }
        }
        _desktopBounds = default;
        if (!NativeMethods.IsIconic(_handle)) NativeMethods.GetWindowRect(_handle, out _desktopBounds);
        var playerSurface = PlayerSurfaceSession.Capture(target);
        var childStyle = (_style.ToInt64() | NativeMethods.WsChild) & ~WindowFrameStyles;
        var styleError = SetWindowStyle(new nint(childStyle));
        if (styleError != 0)
        {
            error = EmbedError(styleError);
            if (restoredBeforeAttach)
            {
                Detach(out var restoreError);
                error += restoreError;
            }
            else _handle = _host = nint.Zero;
            return false;
        }
        Marshal.SetLastPInvokeError(0);
        NativeMethods.SetParent(_handle, host);
        var parentError = Marshal.GetLastPInvokeError();
        if (!IsAttached)
        {
            Detach(out var restoreError);
            error = EmbedError(parentError) + restoreError;
            return false;
        }
        if (!HasVisibleStyle)
            NativeMethods.ShowWindow(_handle, NativeMethods.SwShowNoActivate);
        if (!FitWindow(host, force: true) || !IsVisibleInHost)
        {
            Detach(out var restoreError);
            error = "目标应用没有在广告内显示，已取消嵌入。" + restoreError;
            return false;
        }
        _playerSurface = playerSurface;
        return true;
    }

    private int SetWindowStyle(nint style)
    {
        Marshal.SetLastPInvokeError(0);
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlStyle, style);
        return Marshal.GetLastPInvokeError();
    }

    private static string EmbedError(int error) => error == 5
        ? "Windows 拒绝访问目标窗口（错误 5）。请确认 CatFish 与目标应用以相同权限启动后重试。"
        : $"Windows 未能嵌入此窗口（错误 {error}），可先在目标应用内开启小窗后重试。";

    public void Resize(nint host, bool force = false) => FitWindow(host, force);

    private bool FitWindow(nint host, bool force)
    {
        if (_restoring || host != _host || !IsAttached || NativeMethods.IsHungAppWindow(_handle)
            || !NativeMethods.GetClientRect(host, out var bounds)) return false;
        if (!force && NativeMethods.GetWindowRect(_handle, out var current) && NativeMethods.GetWindowRect(host, out var hostBounds)
            && current.Left == hostBounds.Left && current.Top == hostBounds.Top
            && current.Right - current.Left == bounds.Right && current.Bottom - current.Top == bounds.Bottom) return true;
        if (!NativeMethods.SetWindowPos(_handle, nint.Zero, 0, 0, Math.Max(1, bounds.Right), Math.Max(1, bounds.Bottom),
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoCopyBits | (force ? NativeMethods.SwpFrameChanged : 0)
            | NativeMethods.SwpNoZOrder)) return false;
        NativeMethods.RedrawWindow(_handle, nint.Zero, nint.Zero,
            NativeMethods.RdwInvalidate | NativeMethods.RdwErase | NativeMethods.RdwAllChildren | NativeMethods.RdwFrame);
        return true;
    }

    internal void UpdatePlayerSurfaceLayer(nint popup, bool showAbove)
    {
        if (_playerSurface is not null && !_restoring && IsAlive && NativeMethods.IsWindow(popup))
            _playerSurface.Update(popup, showAbove);
    }

    public bool Detach(out string error, bool restoreForClose = false)
    {
        error = string.Empty;
        _restoring = true;
        if (IsAlive && NativeMethods.SendMessageTimeout(_handle, 0, nint.Zero, nint.Zero, 2, 500, out _) == nint.Zero)
        {
            error = "目标应用暂时未响应，未发送关闭请求。";
            return IsAttached ? false : FinishDetach(ref error);
        }
        if (IsAttached)
        {
            NativeMethods.SetParent(_handle, nint.Zero);
            if (IsAttached)
            { error = "窗口还原失败，请先让目标应用恢复响应或以相同权限运行，再点击还原。"; return false; }
        }
        if (!IsAlive) return FinishDetach(ref error);
        var parent = NativeMethods.GetAncestor(_handle, NativeMethods.GaParent);
        if (parent != nint.Zero && parent != NativeMethods.GetDesktopWindow())
        {
            error = "目标应用已切换到其他窗口，未发送关闭请求。";
            return FinishDetach(ref error);
        }
        var style = _style.ToInt64() & ~(NativeMethods.WsMinimize | NativeMethods.WsMaximize | NativeMethods.WsVisible);
        style |= NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlStyle).ToInt64() & NativeMethods.WsVisible;
        if (!RestoreWindowLong(NativeMethods.GwlStyle, new nint(style), out error)
            || !RestoreWindowLong(NativeMethods.GwlExStyle, _extendedStyle, out error)
            || !RestoreWindowLong(-8, NativeMethods.IsWindow(_owner) ? _owner : nint.Zero, out error)) return FinishDetach(ref error);
        error = RestoreDesktopPlacement(restoreForClose);
        return FinishDetach(ref error);
    }

    private string RestoreDesktopPlacement(bool restoreForClose)
    {
        const uint RestoreToMaximized = 2, AsyncPlacement = 4;
        var placement = _placement;
        placement.Flags &= ~AsyncPlacement;
        if (restoreForClose)
        {
            placement.ShowCommand = placement.ShowCommand == 3 || (placement.Flags & RestoreToMaximized) != 0 ? 3u : 1u;
            placement.Flags &= ~RestoreToMaximized;
        }
        var topmost = (_extendedStyle.ToInt64() & NativeMethods.WsExTopmost) != 0 ? NativeMethods.HwndTopmost : NativeMethods.HwndNoTopmost;
        var placed = NativeMethods.SetWindowPlacement(_handle, ref placement);
        var refreshed = NativeMethods.SetWindowPos(_handle, topmost, 0, 0, 0, 0,
            RestoreFrameFlags | NativeMethods.SwpNoMove | NativeMethods.SwpNoSize);
        var wasVisible = (_style.ToInt64() & NativeMethods.WsVisible) != 0;
        if (!placed)
        {
            if (IsAlive && _desktopBounds.Right > _desktopBounds.Left && _desktopBounds.Bottom > _desktopBounds.Top)
                NativeMethods.SetWindowPos(_handle, topmost, _desktopBounds.Left, _desktopBounds.Top,
                    _desktopBounds.Right - _desktopBounds.Left, _desktopBounds.Bottom - _desktopBounds.Top, RestoreFrameFlags);
            if (IsAlive && (restoreForClose || wasVisible))
                NativeMethods.ShowWindowAsync(_handle, placement.ShowCommand);
        }
        if (!restoreForClose && !wasVisible) NativeMethods.ShowWindow(_handle, 0);
        NativeMethods.RedrawWindow(_handle, nint.Zero, nint.Zero,
            NativeMethods.RdwInvalidate | NativeMethods.RdwErase | NativeMethods.RdwAllChildren | NativeMethods.RdwFrame);
        return placed && refreshed ? string.Empty : "应用已移出广告，位置或尺寸未能完整还原，未发送关闭请求。";
    }

    private bool FinishDetach(ref string error)
    {
        if (IsAttached)
        {
            error = "目标应用仍在广告内，请稍后再次点击还原或关闭。";
            return false;
        }
        if (_playerSurface?.Restore() == false && error.Length == 0)
            error = "应用已移出广告，播放器层级未能还原，未发送关闭请求。";
        _playerSurface = null;
        _handle = _host = nint.Zero;
        _restoring = false;
        return true;
    }

    private bool RestoreWindowLong(int index, nint value, out string error)
    {
        error = string.Empty;
        if (NativeMethods.GetWindowLongPtr(_handle, index) == value) return true;
        Marshal.SetLastSystemError(0);
        var previous = NativeMethods.SetWindowLongPtr(_handle, index, value);
        var nativeError = Marshal.GetLastPInvokeError();
        if (previous != nint.Zero || nativeError == 0 || NativeMethods.GetWindowLongPtr(_handle, index) == value) return true;
        var stage = index == NativeMethods.GwlStyle ? "窗口样式" : index == NativeMethods.GwlExStyle ? "扩展样式" : "窗口归属";
        error = $"还原{stage}失败（Windows 错误 {nativeError}），未发送关闭请求，请确认双方权限一致后重试。";
        return false;
    }
}
