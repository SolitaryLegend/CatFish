using System.Windows.Threading;
using CatFish.Core;

namespace CatFish.Features.Automation;

public sealed record SwitchOptions(int MinSeconds, int MaxSeconds, string Mode, bool PreferDifferent)
{
    public static SwitchOptions FromSettings(AppSettings settings) => new(settings.SwitchMinSeconds, settings.SwitchMaxSeconds, settings.SwitchMode, settings.PreferDifferentWindow);
}

public sealed record ScrollOptions(int MinSeconds, int MaxSeconds, int Rows, bool RandomRows, string Direction, bool MayActivate, bool RandomPause)
{
    public static ScrollOptions FromSettings(AppSettings settings) => new(settings.ScrollMinSeconds, settings.ScrollMaxSeconds,
        settings.ScrollRows, settings.ScrollRandomRows, settings.ScrollDirection, settings.ScrollMayActivateTarget, settings.ScrollRandomPause);
}

public interface IWindowAutomationPlatform
{
    nint ForegroundWindow { get; }
    bool IsAvailable(WindowTarget target);
    bool TryActivate(nint handle);
    bool SendWheel(nint target, int delta);
}

// Application-owned task state. Settings windows only observe and edit this service.
public sealed class WindowAutomationService
{
    private readonly IWindowAutomationPlatform _platform;
    private readonly TimeProvider _clock;
    private readonly Random _random;
    private readonly DispatcherTimer _timer;
    private SwitchOptions _switchOptions = new(30, 90, "Random", true);
    private ScrollOptions _scrollOptions = new(3, 7, 3, true, "Down", false, true);
    private DateTimeOffset _switchDue, _scrollDue;
    private nint _lastSwitched;
    private bool _alternatingDown = true;
    private int _scrollGeneration, _scrollCount;
    private string _switchResult = "", _scrollResult = "";
    public bool IsSwitching { get; private set; }
    public bool IsScrolling { get; private set; }
    public IReadOnlyList<WindowTarget> SwitchTargets { get; private set; } = Array.Empty<WindowTarget>();
    public WindowTarget? ScrollTarget { get; private set; }
    public string SwitchStatus { get; private set; } = "已停止";
    public string ScrollStatus { get; private set; } = "已停止";
    public event EventHandler? Changed;

    public WindowAutomationService(IWindowAutomationPlatform? platform = null, TimeProvider? clock = null, Random? random = null)
    {
        _platform = platform ?? new WindowsAutomationPlatform(); _clock = clock ?? TimeProvider.System; _random = random ?? Random.Shared;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
    }

    public void SetSwitchTargets(IEnumerable<WindowTarget> targets)
    {
        SwitchTargets = targets.DistinctBy(t => t.Handle).ToArray();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetScrollTarget(WindowTarget? target)
    {
        ScrollTarget = target; _scrollGeneration++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool StartSwitching(SwitchOptions options)
    {
        if (IsSwitching) return true;
        if (SwitchTargets.Count == 0) { SetSwitchStatus("请先选择切换应用。"); return false; }
        if (options.MinSeconds < 10 || options.MaxSeconds > 180 || options.MaxSeconds < options.MinSeconds)
        { SetSwitchStatus("切换间隔请输入 10–180 秒，最小值不能大于最大值。"); return false; }
        _switchOptions = options; IsSwitching = true; _lastSwitched = nint.Zero;
        ScheduleSwitch(); _timer.Start(); SwitchNext(); UpdateCountdowns(); return true;
    }

    public void StopSwitching()
    {
        IsSwitching = false; _switchResult = ""; SetSwitchStatus("窗口切换已停止。"); StopTimerIfIdle();
    }

    public bool SwitchNext()
    {
        var available = SwitchTargets.Where(_platform.IsAvailable).ToList();
        if (available.Count == 0) { _switchResult = "等待可用的所选窗口"; SetSwitchStatus(_switchResult); return false; }
        var foreground = _platform.ForegroundWindow;
        var avoid = available.Any(t => t.Handle == foreground) ? foreground : _lastSwitched;
        if (_switchOptions.Mode is "Ordered" or "Manual")
        {
            var previous = available.FindIndex(t => t.Handle == avoid);
            available = available.Skip(previous + 1).Concat(available.Take(previous + 1)).ToList();
            if (_switchOptions.PreferDifferent && available.Count > 1) available.RemoveAll(t => t.Handle == avoid);
        }
        else
        {
            if (_switchOptions.PreferDifferent && available.Count > 1) available.RemoveAll(t => t.Handle == avoid);
            _random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(available));
        }
        foreach (var target in available)
        {
            if (!_platform.TryActivate(target.Handle) || _platform.ForegroundWindow != target.Handle) continue;
            _lastSwitched = target.Handle; _switchResult = $"当前：{target.DisplayName}";
            SetSwitchStatus(_switchResult); return true;
        }
        _switchResult = "目标未能获得焦点，将继续重试"; SetSwitchStatus(_switchResult); return false;
    }

    public bool StartScrolling(ScrollOptions options)
    {
        if (IsScrolling) return true;
        if (ScrollTarget is null) { SetScrollStatus("请先选择内容滚动应用。"); return false; }
        if (options.MinSeconds < 1 || options.MaxSeconds > 600 || options.MaxSeconds < options.MinSeconds || options.Rows is < 1 or > 10)
        { SetScrollStatus("滚动间隔请输入 1–600 秒，行数请输入 1–10。"); return false; }
        _scrollOptions = options; IsScrolling = true; _scrollGeneration++; _scrollCount = 0; _alternatingDown = true;
        _scrollResult = ""; ScheduleScroll(); _timer.Start(); UpdateCountdowns(); return true;
    }

    public void StopScrolling()
    {
        _scrollGeneration++; IsScrolling = false; _scrollResult = ""; SetScrollStatus("内容滚动已停止。"); StopTimerIfIdle();
    }

    public void StopAll() { StopSwitching(); StopScrolling(); }

    internal void Tick()
    {
        var now = _clock.GetUtcNow();
        if (IsSwitching && _switchOptions.Mode != "Manual" && now >= _switchDue) { ScheduleSwitch(); SwitchNext(); }
        if (IsScrolling && now >= _scrollDue) { ScheduleScroll(); ScrollOnce(); }
        UpdateCountdowns();
    }

    private void ScrollOnce()
    {
        var generation = _scrollGeneration;
        if (ScrollTarget is not { } target || !_platform.IsAvailable(target)) { _scrollResult = "等待所选内容窗口可用"; return; }
        if (_platform.ForegroundWindow != target.Handle)
        {
            if (!_scrollOptions.MayActivate) { _scrollResult = "等待内容窗口获得焦点"; return; }
            if (!_platform.TryActivate(target.Handle)) { _scrollResult = "目标未能获得焦点，已跳过本次滚动"; return; }
        }
        if (!IsScrolling || generation != _scrollGeneration || _platform.ForegroundWindow != target.Handle) return;
        var rows = _scrollOptions.RandomRows ? _random.Next(1, _scrollOptions.Rows + 1) : _scrollOptions.Rows;
        var direction = _scrollOptions.Direction switch { "Up" => 1, "Alternate" => _alternatingDown ? -1 : 1, _ => -1 };
        if (!_platform.SendWheel(target.Handle, direction * rows * NativeMethods.WheelDelta))
        { _scrollResult = "滚动发送失败，将继续重试"; return; }
        if (_scrollOptions.Direction == "Alternate") _alternatingDown = !_alternatingDown;
        _scrollCount++; _scrollResult = $"已滚动 {_scrollCount} 次 · 本次 {rows} 行";
        if (_scrollOptions.RandomPause && _scrollCount % 5 == 0) _scrollDue += TimeSpan.FromSeconds(_random.Next(1, 3));
    }

    private void ScheduleSwitch() => _switchDue = _clock.GetUtcNow().AddSeconds(_random.Next(_switchOptions.MinSeconds, _switchOptions.MaxSeconds + 1));
    private void ScheduleScroll() => _scrollDue = _clock.GetUtcNow().AddSeconds(_random.Next(_scrollOptions.MinSeconds, _scrollOptions.MaxSeconds + 1));
    private void StopTimerIfIdle() { if (!IsSwitching && !IsScrolling) _timer.Stop(); }
    private void UpdateCountdowns()
    {
        if (IsSwitching) SetSwitchStatus(_switchOptions.Mode == "Manual" ? $"手动模式就绪 · {_switchResult}"
            : $"运行中 · {Math.Max(0, (int)Math.Ceiling((_switchDue - _clock.GetUtcNow()).TotalSeconds))} 秒后切换\n{_switchResult}");
        if (IsScrolling) SetScrollStatus($"运行中 · {Math.Max(0, (int)Math.Ceiling((_scrollDue - _clock.GetUtcNow()).TotalSeconds))} 秒后滚动\n{_scrollResult}");
    }
    private void SetSwitchStatus(string value) { if (SwitchStatus == value) return; SwitchStatus = value; Changed?.Invoke(this, EventArgs.Empty); }
    private void SetScrollStatus(string value) { if (ScrollStatus == value) return; ScrollStatus = value; Changed?.Invoke(this, EventArgs.Empty); }
}
