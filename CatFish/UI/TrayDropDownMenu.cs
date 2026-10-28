using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace CatFish.UI;

// Anchor to the popup edge, rather than the padded item's suggested dropdown location.
internal sealed class TrayDropDownMenu : Forms.ToolStripDropDownMenu
{
    public TrayDropDownMenu() { DoubleBuffered = true; }

    protected override void WndProc(ref Forms.Message message)
    {
        base.WndProc(ref message);
        // ToolStripDropDown recalculates managed bounds after SetBoundsCore. Correct the final
        // native position before the popup is painted, so there is no visible repositioning.
        if (message.Msg != 0x0046 || message.LParam == nint.Zero) return; // WM_WINDOWPOSCHANGING
        var position = Marshal.PtrToStructure<WindowPosition>(message.LParam);
        var width = (position.Flags & 0x0001) != 0 ? Width : position.Width;
        var height = (position.Flags & 0x0001) != 0 ? Height : position.Height;
        if (OwnerItem is { Owner: Forms.ToolStripDropDown { Visible: true } parent } item && width > 0 && height > 0)
        {
            var row = parent.RectangleToScreen(item.Bounds);
            var area = Forms.Screen.FromRectangle(parent.Bounds).WorkingArea;
            var opensLeft = parent is TrayDropDownMenu && parent.OwnerItem?.Owner is Forms.ToolStripDropDown ancestor
                && parent.Left < ancestor.Left;
            var right = parent.Right - 1;
            var left = parent.Left - width + 1;
            var x = opensLeft && left >= area.Left || right + width > area.Right && left >= area.Left ? left : right;
            position.X = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
            position.Y = Math.Clamp(row.Top - Padding.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
            // On first display Windows can use the creation coordinates and send only a
            // show/resize notification with SWP_NOMOVE. Anchor that notification as well.
            position.Flags &= ~0x0002u; // SWP_NOMOVE
            Marshal.StructureToPtr(position, message.LParam, false);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public nint Window, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }
}
