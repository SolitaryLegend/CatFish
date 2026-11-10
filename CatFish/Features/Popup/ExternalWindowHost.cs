using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using CatFish.Core;

namespace CatFish.Features.Popup;

public sealed class ExternalWindowHost : HwndHost
{
    public EmbeddedWindowSession Session { get; } = new();
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var handle = NativeMethods.CreateWindowEx(0, "static", string.Empty,
            (uint)(NativeMethods.WsChild | NativeMethods.WsVisible | NativeMethods.WsClipChildren | NativeMethods.WsClipSiblings),
            0, 0, 1, 1, parent.Handle, nint.Zero, nint.Zero, nint.Zero);
        if (handle == nint.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        return new(this, handle);
    }
    protected override void OnWindowPositionChanged(Rect bounds)
    {
        base.OnWindowPositionChanged(bounds); Session.Resize(Handle);
    }
    protected override void DestroyWindowCore(HandleRef handle)
    {
        if (Session.Detach(out _)) NativeMethods.DestroyWindow(handle.Handle);
    }
}
