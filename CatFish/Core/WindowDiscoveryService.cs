using System.Diagnostics;
using System.Text;

namespace CatFish.Core;

public sealed record WindowTarget(nint Handle, string Title, string ProcessName, int ProcessId, bool IsCatFish = false, string? ApplicationName = null)
{ public string DisplayName => !string.IsNullOrWhiteSpace(Title) ? Title : ApplicationName ?? ProcessName; }

public sealed class WindowDiscoveryService
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "winlogon", "lsass", "services", "smss", "wininit", "dwm", "fontdrvhost", "sihost",
        "TextInputHost", "InputApp", "SearchHost", "SearchApp", "StartMenuExperienceHost", "ShellExperienceHost", "LockApp",
        "NVIDIA Share", "NVIDIA Overlay", "nvcontainer", "GameBar", "GameBarFTServer", "GameBarPresenceWriter"
    };
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "tooltips_class32"
    };

    public static bool IsUserWindow(nint handle, bool includeFloating = false)
    {
        if (handle == nint.Zero || !NativeMethods.IsWindow(handle) || !NativeMethods.IsWindowVisible(handle)
            || (!includeFloating && NativeMethods.GetWindow(handle, NativeMethods.GwOwner) != nint.Zero)
            || handle == NativeMethods.GetShellWindow() || handle == NativeMethods.GetDesktopWindow()) return false;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlStyle).ToInt64();
        var extendedStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        if ((style & NativeMethods.WsChild) != 0
            || (!includeFloating && (extendedStyle & (NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate)) != 0)) return false;

        // WS_VISIBLE can remain set on suspended UWP and other DWM-cloaked windows.
        // https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
        if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0
            && cloaked != 0) return false;
        var windowClass = new StringBuilder(256); NativeMethods.GetClassName(handle, windowClass, windowClass.Capacity);
        if (ExcludedClasses.Contains(windowClass.ToString())) return false;
        return NativeMethods.GetWindowRect(handle, out var bounds) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }

    public IReadOnlyList<WindowTarget> GetEligibleWindows(nint mainWindow = default, bool includeFloating = false)
    {
        var list = new List<WindowTarget>();
        NativeMethods.EnumWindows((h, _) =>
        {
            if (!IsUserWindow(h, includeFloating)) return true;
            if (mainWindow != nint.Zero && h == mainWindow) return true;
            var len = NativeMethods.GetWindowTextLength(h); if (len <= 0) return true;
            var buffer = new StringBuilder(Math.Min(len + 1, 2048)); NativeMethods.GetWindowText(h, buffer, buffer.Capacity);
            NativeMethods.GetWindowThreadProcessId(h, out var rawPid); var pid = unchecked((int)rawPid);
            if (pid <= 0 || pid == Environment.ProcessId) return true;
            try
            {
                using var p = Process.GetProcessById(pid); var title = buffer.ToString().Trim();
                if (!Excluded.Contains(p.ProcessName) && title.Length > 0)
                {
                    list.Add(new(h, title, p.ProcessName, pid));
                }
            }
            catch (ArgumentException) { } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            return true;
        }, nint.Zero);
        if (mainWindow != nint.Zero && IsUserWindow(mainWindow))
        {
            var title = new StringBuilder(2048); NativeMethods.GetWindowText(mainWindow, title, title.Capacity);
            list.Insert(0, new(mainWindow, title.ToString(), "CatFish", Environment.ProcessId, true));
        }
        return list.OrderBy(x => x.IsCatFish ? string.Empty : x.ProcessName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
