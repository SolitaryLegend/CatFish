using System.Text;
using CatFish.Core;

namespace CatFish.Features.Popup;

internal static class TencentVideoWindows
{
    private const string ProcessName = "QQLive";
    private const string SurfaceTitle = "ViewsPlayerPluginWnd";

    internal static bool IsApplication(WindowTarget target) =>
        target.ProcessName.Equals(ProcessName, StringComparison.OrdinalIgnoreCase);

    internal static bool HasApplicationTitle(WindowTarget target) => target.Title is "腾讯视频" or "腾讯视频.exe";

    internal static bool IsSurface(WindowTarget target) =>
        IsApplication(target) && target.Title.Equals(SurfaceTitle, StringComparison.OrdinalIgnoreCase);

    internal static bool IsSurface(nint handle)
    {
        var title = new StringBuilder(SurfaceTitle.Length + 2);
        NativeMethods.GetWindowText(handle, title, title.Capacity);
        return title.ToString().Equals(SurfaceTitle, StringComparison.OrdinalIgnoreCase);
    }

    internal static nint FindSurface(nint owner, uint processId)
    {
        nint surface = nint.Zero;
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (NativeMethods.GetWindow(handle, NativeMethods.GwOwner) != owner
                || !NativeMethods.IsWindowVisible(handle)
                || NativeMethods.GetWindowThreadProcessId(handle, out var pid) == 0
                || pid != processId || !IsSurface(handle)) return true;
            surface = handle;
            return false;
        }, nint.Zero);
        return surface;
    }
}
