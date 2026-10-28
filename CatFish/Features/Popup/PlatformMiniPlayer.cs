using System.Windows.Automation;
using CatFish.Core;

namespace CatFish.Features.Popup;

public static class PlatformMiniPlayer
{
    private static readonly string[] Names = ["小窗", "小窗口", "小窗播放", "小窗口播放", "小窗模式", "进入小窗", "开启小窗", "切换小窗", "画中画", "画中画模式", "开启画中画", "进入画中画", "Picture in picture", "Mini player"];

    public static bool TryInvoke(nint handle, CancellationToken cancellationToken = default)
    {
        try
        {
            var root = AutomationElement.FromHandle(handle);
            var condition = new AndCondition(new PropertyCondition(AutomationElement.IsEnabledProperty, true),
                new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Custom)));
            foreach (AutomationElement button in root.FindAll(TreeScope.Descendants, condition))
            {
                if (cancellationToken.IsCancellationRequested) return false;
                if (!IsEntryLabel(button.Current.Name) && !IsEntryLabel(button.Current.HelpText)) continue;
                if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
                {
                    if (cancellationToken.IsCancellationRequested) return false;
                    ((InvokePattern)pattern).Invoke(); return true;
                }
                if (button.TryGetCurrentPattern(TogglePattern.Pattern, out var togglePattern))
                {
                    var toggle = (TogglePattern)togglePattern;
                    if (cancellationToken.IsCancellationRequested) return false;
                    if (toggle.Current.ToggleState == ToggleState.Off) { toggle.Toggle(); return true; }
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException
            or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
        return false;
    }

    private static bool IsEntryLabel(string label)
    {
        var normalized = string.Concat(label.Where(character => !char.IsWhiteSpace(character))).Replace('-', ' ').Replace(" ", "");
        return Names.Any(name =>
        {
            var expected = name.Replace(" ", "");
            return normalized.Equals(expected, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(expected + "(", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(expected + "（", StringComparison.OrdinalIgnoreCase);
        });
    }

    public static bool IsMiniPlayer(WindowTarget target) => target.Title.Contains("小窗", StringComparison.OrdinalIgnoreCase)
        || target.Title.Contains("画中画", StringComparison.OrdinalIgnoreCase)
        || target.Title.Contains("mini player", StringComparison.OrdinalIgnoreCase)
        || target.Title.Contains("picture in picture", StringComparison.OrdinalIgnoreCase);
}
