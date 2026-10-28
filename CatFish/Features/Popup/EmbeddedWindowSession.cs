using System.Runtime.InteropServices;
using CatFish.Core;

namespace CatFish.Features.Popup;

public sealed class EmbeddedWindowSession
{
    private nint _handle, _owner, _style, _extendedStyle;
    private uint _processId;
    private NativeMethods.WindowPlacement _placement;
    private bool _restoring;
    public nint Handle => IsAlive ? _handle : nint.Zero;
    public bool IsAlive => _handle != nint.Zero && NativeMethods.IsWindow(_handle)
        && NativeMethods.GetWindowThreadProcessId(_handle, out var processId) != 0 && processId == _processId;

    public bool Attach(WindowTarget target, nint host, out string error)
    {
        error = string.Empty;
        if (!NativeMethods.IsWindow(host) || !NativeMethods.IsWindow(target.Handle)
            || NativeMethods.GetWindowThreadProcessId(target.Handle, out var processId) == 0
            || processId != target.ProcessId || (NativeMethods.GetWindowLongPtr(target.Handle, NativeMethods.GwlStyle).ToInt64() & NativeMethods.WsChild) != 0)
        { error = "目标窗口已关闭或已嵌入其他页面，请刷新列表。"; return false; }
        if (!Detach(out error)) return false;
        _handle = target.Handle; _processId = processId; _restoring = false;
        _style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlStyle);
        _extendedStyle = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        _owner = NativeMethods.GetWindow(_handle, NativeMethods.GwOwner);
        _placement = new() { Length = Marshal.SizeOf<NativeMethods.WindowPlacement>() };
        if (!NativeMethods.GetWindowPlacement(_handle, ref _placement))
        { _handle = nint.Zero; error = "无法读取窗口位置，未更改目标窗口。"; return false; }
        if ((_style.ToInt64() & (NativeMethods.WsMinimize | NativeMethods.WsMaximize)) != 0
            && NativeMethods.SendMessageTimeout(_handle, 0x112, new nint(0xF120), nint.Zero, 2, 500, out _) == nint.Zero)
        { _handle = nint.Zero; error = "目标窗口未响应还原操作，请先手动还原窗口后重试。"; return false; }
        var childStyle = (_style.ToInt64() | NativeMethods.WsChild) & ~(NativeMethods.WsPopup | NativeMethods.WsCaption | NativeMethods.WsThickFrame | 0x210B0000L);
        Marshal.SetLastPInvokeError(0);
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlStyle, new nint(childStyle));
        var styleError = Marshal.GetLastPInvokeError();
        Marshal.SetLastPInvokeError(0);
        NativeMethods.SetParent(_handle, host);
        var parentError = Marshal.GetLastPInvokeError();
        if (styleError != 0 || NativeMethods.GetParent(_handle) != host)
        {
            Detach(out var restoreError);
            error = $"Windows 拒绝嵌入此窗口（{styleError}/{parentError}），请确认双方权限一致；也可先在平台内开启小窗后再选择。" + restoreError;
            return false;
        }
        Resize(host, true); return true;
    }

    public void Resize(nint host, bool force = false)
    {
        if (_restoring || !IsAlive || NativeMethods.IsHungAppWindow(_handle) || !NativeMethods.GetClientRect(host, out var bounds)) return;
        if (!force && NativeMethods.GetWindowRect(_handle, out var current) && NativeMethods.GetWindowRect(host, out var hostBounds)
            && current.Left == hostBounds.Left && current.Top == hostBounds.Top
            && current.Right - current.Left == bounds.Right && current.Bottom - current.Top == bounds.Bottom) return;
        NativeMethods.SetWindowPos(_handle, nint.Zero, 0, 0, Math.Max(1, bounds.Right), Math.Max(1, bounds.Bottom),
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoCopyBits | (force ? NativeMethods.SwpFrameChanged : 0) | 0x4);
        NativeMethods.RedrawWindow(_handle, nint.Zero, nint.Zero,
            NativeMethods.RdwInvalidate | NativeMethods.RdwErase | NativeMethods.RdwAllChildren | NativeMethods.RdwFrame);
    }

    public bool Detach(out string error, bool restoreForClose = false)
    {
        error = string.Empty;
        if (!IsAlive) { _handle = nint.Zero; return true; }
        _restoring = true;
        if (NativeMethods.SendMessageTimeout(_handle, 0, nint.Zero, nint.Zero, 2, 500, out _) == nint.Zero)
        { error = "目标应用暂时未响应，未发送关闭请求，请稍后再次点击还原或关闭。"; return false; }
        if ((NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlStyle).ToInt64() & NativeMethods.WsChild) != 0)
        {
            NativeMethods.SetParent(_handle, nint.Zero);
            var parent = NativeMethods.GetParent(_handle);
            if (parent != nint.Zero && parent != NativeMethods.GetDesktopWindow())
            { error = "窗口还原失败，请先让目标应用恢复响应或以相同权限运行，再点击还原。"; return false; }
        }
        var style = _style.ToInt64() & ~(NativeMethods.WsMinimize | NativeMethods.WsMaximize | NativeMethods.WsVisible);
        style |= NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlStyle).ToInt64() & NativeMethods.WsVisible;
        if (!RestoreWindowLong(NativeMethods.GwlStyle, new nint(style), out error)
            || !RestoreWindowLong(NativeMethods.GwlExStyle, _extendedStyle, out error)
            || !RestoreWindowLong(-8, NativeMethods.IsWindow(_owner) ? _owner : nint.Zero, out error)) return false;
        var placement = _placement;
        placement.Flags &= ~4u;
        if (restoreForClose)
        {
            placement.ShowCommand = placement.ShowCommand == 3 || (placement.Flags & 2) != 0 ? 3u : 1u;
            placement.Flags &= ~2u;
        }
        if (!NativeMethods.SetWindowPlacement(_handle, ref placement)
            || !NativeMethods.SetWindowPos(_handle, (_extendedStyle.ToInt64() & NativeMethods.WsExTopmost) != 0 ? NativeMethods.HwndTopmost : NativeMethods.HwndNoTopmost,
                0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged | NativeMethods.SwpNoCopyBits))
        { error = "应用位置或尺寸尚未还原，未发送关闭请求，请稍后重试。"; return false; }
        if (!restoreForClose && (_style.ToInt64() & NativeMethods.WsVisible) == 0) NativeMethods.ShowWindow(_handle, 0);
        NativeMethods.RedrawWindow(_handle, nint.Zero, nint.Zero,
            NativeMethods.RdwInvalidate | NativeMethods.RdwErase | NativeMethods.RdwAllChildren | NativeMethods.RdwFrame);
        _handle = nint.Zero; _restoring = false; return true;
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
