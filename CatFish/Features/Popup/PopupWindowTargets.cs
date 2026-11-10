using CatFish.Core;

namespace CatFish.Features.Popup;

internal static class PopupWindowTargets
{
    internal static IReadOnlyList<WindowTarget> Discover() =>
        new WindowDiscoveryService().GetEligibleWindows(includeFloating: true);

    internal static IReadOnlyList<WindowTarget> GetPickerTargets() => FilterPickerTargets(Discover());

    internal static IReadOnlyList<WindowTarget> FilterPickerTargets(IReadOnlyList<WindowTarget> targets)
    {
        var tencent = GetPreferredTencentWindow(targets);
        var excludedYouku = GetYoukuPickerExclusions(targets);
        return targets.Where(target => !excludedYouku.Contains(target.Handle)
            && (target == tencent || (!TencentVideoWindows.IsApplication(target)
                && (tencent is null || !TencentVideoWindows.HasApplicationTitle(target))))).ToList();
    }

    internal static WindowTarget ForEmbedding(WindowTarget target)
    {
        var owner = YoukuWindows.GetPlaybackOwner(target);
        return owner == nint.Zero ? target : target with { Handle = owner };
    }

    internal static WindowTarget? ResolveDropTarget(nint handle, IReadOnlyList<WindowTarget> targets)
    {
        var target = targets.FirstOrDefault(window => window.Handle == handle);
        if (target is not null && !TencentVideoWindows.IsSurface(target)) return target;

        // Tencent Video's owned player surface can cover the whole window and receive the drag.
        var owner = NativeMethods.GetWindow(handle, NativeMethods.GwOwner);
        var ownerTarget = targets.FirstOrDefault(window => window.Handle == owner && TencentVideoWindows.IsApplication(window));
        if (ownerTarget is null || NativeMethods.GetWindowThreadProcessId(handle, out var processId) == 0
            || processId != ownerTarget.ProcessId) return null;
        return TencentVideoWindows.IsSurface(handle) ? ownerTarget : null;
    }

    private static WindowTarget? GetPreferredTencentWindow(IReadOnlyList<WindowTarget> targets)
    {
        var players = targets.Where(TencentVideoWindows.IsSurface)
            .Select(surface => NativeMethods.GetWindow(surface.Handle, NativeMethods.GwOwner)).ToHashSet();
        var windows = targets.Where(target => TencentVideoWindows.IsApplication(target)
            && !TencentVideoWindows.IsSurface(target)).ToList();
        return windows.FirstOrDefault(target => players.Contains(target.Handle) && !NativeMethods.IsIconic(target.Handle))
            ?? windows.FirstOrDefault(target => !NativeMethods.IsIconic(target.Handle))
            ?? windows.FirstOrDefault();
    }

    private static HashSet<nint> GetYoukuPickerExclusions(IReadOnlyList<WindowTarget> targets)
    {
        var excluded = new HashSet<nint>();
        foreach (var target in targets.Where(YoukuWindows.IsApplication))
        {
            var owner = YoukuWindows.GetPlaybackOwner(target);
            if (owner == nint.Zero) continue;

            // Use the playback title for the pair. An already hosted pair is not selectable.
            excluded.Add(owner);
            if ((NativeMethods.GetWindowLongPtr(owner, NativeMethods.GwlStyle).ToInt64() & NativeMethods.WsChild) != 0)
                excluded.Add(target.Handle);
        }
        return excluded;
    }
}
