using System.Runtime.InteropServices;
using System.Windows;

namespace CatFish.Core;

public sealed record MonitorBounds(string Name, Rect Bounds, Rect WorkArea, bool IsPrimary);

public static class MonitorService
{
    public static IReadOnlyList<MonitorBounds> GetMonitors()
    {
        var result = new List<MonitorBounds>();
        NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, (handle, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (NativeMethods.GetMonitorInfo(handle, ref info))
            {
                var b = info.Monitor; var w = info.Work;
                result.Add(new($"Display {result.Count + 1}", new Rect(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top), new Rect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top), (info.Flags & 1) != 0));
            }
            return true;
        }, nint.Zero);
        return result.OrderByDescending(x => x.IsPrimary).ToList();
    }
}
