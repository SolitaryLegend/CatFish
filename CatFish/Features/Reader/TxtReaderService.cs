using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Threading;
using System.Windows.Media;
using Microsoft.Win32;

namespace CatFish.Features.Reader;

/// <summary>Owns playback independently of the settings window and other automation modules.</summary>
public sealed class TxtReaderService : IDisposable
{
    private readonly Func<Task> _save;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly List<int> _previous = new();
    private CancellationTokenSource? _load;
    private double _elapsed, _sinceSave;
    private bool _disposed;
    private bool _viewportInitialized;
    private bool _renderingSubscribed;
    private TimeSpan? _lastRenderingTime;
    private double _scrollDistance = 1;
    public TxtReaderSettings Settings { get; }
    public TxtDocument? Document { get; private set; }
    public TxtPage? CurrentPage { get; private set; }
    public TxtPage? FollowingPage { get; private set; }
    public int Offset { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsLoading { get; private set; }
    public double ScrollOffset { get; private set; }
    public double ViewportWidth { get; private set; } = 740;
    public Func<string, double> MeasureText { get; set; } = text => text.Length * 22d;
    public string Status { get; private set; } = "选择或拖入 TXT 文件，开始单行阅读。";
    public event EventHandler? Changed;
    public event EventHandler? Frame;
    public event EventHandler? HideRequested;

    public TxtReaderService(TxtReaderSettings settings, Func<Task> save)
    {
        Settings = settings; Settings.Normalize(); _save = save;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += Tick;
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerModeChanged;
    }

    public async Task<bool> OpenAsync(string path, string encoding = "Auto", Func<TxtReadingRecord, int?>? changedFileChoice = null)
    {
        if (_disposed) return false;
        _load?.Cancel(); _load?.Dispose(); var request = _load = new CancellationTokenSource();
        IsLoading = true; Status = "正在读取文本并建立字符索引…"; OnChanged();
        try
        {
            var loaded = await TxtDocument.LoadAsync(path, encoding, request.Token);
            if (request.IsCancellationRequested || _disposed) return false;
            Snapshot();
            var record = Settings.RecentFiles.FirstOrDefault(r => string.Equals(r.Path, loaded.Path, StringComparison.OrdinalIgnoreCase));
            var target = record?.Offset ?? 0;
            if (Document is not null && string.Equals(Document.Path, loaded.Path, StringComparison.OrdinalIgnoreCase)) target = Offset;
            var changed = record is not null && (record.FileSize != loaded.FileSize || record.LastWriteUtc != loaded.LastWriteUtc);
            if (changed)
            {
                var choice = changedFileChoice?.Invoke(record!);
                if (changedFileChoice is not null && choice is null) { Status = "已取消打开；当前阅读内容已保留。"; return false; }
                target = choice ?? target;
            }
            if (request.IsCancellationRequested || _disposed) return false;
            Snapshot(); PauseCore();
            Document = loaded; Offset = loaded.SnapOffset(Math.Clamp(target, 0, loaded.Text.Length - 1));
            _previous.Clear(); ResetTiming();
            record ??= new TxtReadingRecord { Path = loaded.Path };
            Settings.RecentFiles.RemoveAll(r => string.Equals(r.Path, loaded.Path, StringComparison.OrdinalIgnoreCase));
            record.Encoding = encoding; record.FileSize = loaded.FileSize; record.LastWriteUtc = loaded.LastWriteUtc;
            record.LastOpenedUtc = DateTime.UtcNow; record.Offset = Offset;
            Settings.RecentFiles.Insert(0, record); Settings.RecentFiles = Settings.RecentFiles.Take(10).ToList();
            Settings.LastFile = loaded.Path; RebuildPages();
            Status = $"已打开 {System.IO.Path.GetFileName(loaded.Path)} · 已暂停" +
                (changed ? " · 文本已更新" : "") + (loaded.IsLarge ? " · 大文件模式（按需分页，上限 32 MB）" : "");
            await PersistAsync(); return !request.IsCancellationRequested && !_disposed;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Status = ex switch
            {
                FileNotFoundException or DirectoryNotFoundException => "文件已移动或删除，请重新定位；原阅读进度已保留。",
                UnauthorizedAccessException => "没有读取权限，请选择可访问的 TXT 文件。",
                DecoderFallbackException => "无法正确解码文本，请将文件转换为 UTF-8 后重试。",
                _ => "打开失败：" + ex.Message
            };
            return false;
        }
        finally { if (ReferenceEquals(_load, request)) { IsLoading = false; OnChanged(); } }
    }

    public void SetViewport(double width, Func<string, double> measure)
    {
        MeasureText = measure;
        if (_viewportInitialized && Math.Abs(ViewportWidth - width) < .5) return;
        _viewportInitialized = true;
        ViewportWidth = Math.Max(80, width); RefreshLayout();
    }

    public void RefreshLayout(bool repaginate = true, bool persist = true)
    {
        Settings.Normalize();
        if (repaginate) { _previous.Clear(); ResetTiming(); RebuildPages(); }
        else Frame?.Invoke(this, EventArgs.Empty);
        UpdatePlaybackClock();
        OnChanged(); if (persist) _ = PersistAsync();
    }

    private void RebuildPages()
    {
        CurrentPage = Document?.Page(Offset, Settings.MaxCharacters, ViewportWidth, MeasureText, Settings.PreferNaturalBreaks, Settings.CompressWhitespace);
        FollowingPage = Document is not null && CurrentPage is { } page && page.End < Document.Text.Length
            ? Document.Page(page.End, Settings.MaxCharacters, ViewportWidth, MeasureText, Settings.PreferNaturalBreaks, Settings.CompressWhitespace) : null;
        _scrollDistance = Math.Max(1, Math.Min(ViewportWidth, CurrentPage is { } current ? MeasureText(current.Text) : 0)) + 24;
        Frame?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayback() { if (IsPlaying) Pause(); else Play(); }
    public void Play()
    {
        if (_disposed || Document is null || CurrentPage is null || IsPlaying) return;
        IsPlaying = true; Status = "正在自动阅读"; _clock.Restart();
        UpdatePlaybackClock(); OnChanged();
    }
    private void PauseCore() { IsPlaying = false; UpdatePlaybackClock(); _clock.Reset(); }
    private void UpdatePlaybackClock()
    {
        var render = IsPlaying && Settings.Mode == "Scroll";
        if (render != _renderingSubscribed)
        {
            if (render) CompositionTarget.Rendering += Rendering;
            else CompositionTarget.Rendering -= Rendering;
            _renderingSubscribed = render; _lastRenderingTime = null;
        }
        if (IsPlaying && !render) _timer.Start(); else _timer.Stop();
    }
    private void Rendering(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs frame) AdvanceRendering(frame.RenderingTime);
    }
    internal void AdvanceRendering(TimeSpan timestamp)
    {
        if (!_renderingSubscribed) return;
        var previous = _lastRenderingTime; _lastRenderingTime = timestamp;
        // WPF can raise more than one callback for a frame; advance only once per presentation time.
        if (previous is { } time && timestamp > time) Advance((timestamp - time).TotalSeconds);
    }
    public void Pause() { PauseCore(); Status = "已暂停，保留当前位置"; OnChanged(); _ = PersistAsync(); }
    public void EmergencyStop()
    {
        _load?.Cancel(); IsLoading = false; Pause();
    }
    public void Stop() { PauseCore(); ResetTiming(); Status = "已停止，保留当前段落"; OnChanged(); _ = PersistAsync(); }
    private void ResetTiming() { _elapsed = 0; ScrollOffset = 0; if (IsPlaying) _clock.Restart(); Frame?.Invoke(this, EventArgs.Empty); }

    public void Next(bool manual = true)
    {
        if (Document is null || CurrentPage is null) return;
        if (FollowingPage is null) { Finish(); return; }
        _previous.Add(Offset); if (_previous.Count > 4096) _previous.RemoveAt(0);
        Offset = CurrentPage.End; ResetTiming(); RebuildPages();
        if (manual) ManualNavigation();
        OnChanged();
    }

    public void Previous()
    {
        if (Document is null || Offset == 0) return;
        if (_previous.Count > 0) { Offset = _previous[^1]; _previous.RemoveAt(_previous.Count - 1); }
        else
        {
            // After seeking/reflow, walk at most one screen backwards, preserving a Unicode boundary.
            var currentIndex = Document.ElementIndex(Offset); var candidate = Offset;
            for (var i = currentIndex - 1; i >= Math.Max(0, currentIndex - Settings.MaxCharacters); i--)
            {
                var start = Document.OffsetAtElement(i);
                var page = Document.Page(start, Settings.MaxCharacters, ViewportWidth, MeasureText, false, Settings.CompressWhitespace);
                if (page.End < Offset) break;
                candidate = start;
            }
            Offset = candidate == Offset ? Document.OffsetAtElement(currentIndex - 1) : candidate;
        }
        ResetTiming(); RebuildPages(); ManualNavigation(); OnChanged();
    }

    public void Seek(int offset)
    {
        if (Document is null) return;
        Offset = Document.SnapOffset(Math.Clamp(offset, 0, Document.Text.Length - 1)); _previous.Clear();
        ResetTiming(); RebuildPages(); ManualNavigation(); OnChanged();
    }
    public void SeekPercent(double percentage)
    {
        if (Document is not null && double.IsFinite(percentage))
            Seek(Document.OffsetAtElement((int)Math.Round(Math.Clamp(percentage, 0, 100) / 100 * (Document.TotalElements - 1))));
    }
    public bool TrySeekText(string text, out string message)
    {
        if (string.IsNullOrWhiteSpace(text) || new StringInfo(text).LengthInTextElements is < 10 or > 30)
        { message = "请输入 10～30 字的文字内容。"; return false; }
        if (Document is null)
        { message = "请先打开 TXT 文件，再进行文字定位。"; return false; }
        var offset = Document.Text.IndexOf(text, StringComparison.Ordinal);
        if (offset < 0)
        { message = "未找到匹配内容，请检查输入文字是否与原文一致。"; return false; }
        Seek(offset);
        message = $"已定位到首个匹配处（原文字符位置：{Offset:N0}）。";
        return true;
    }
    private void ManualNavigation() { if (Settings.PauseOnManualNavigation) PauseCore(); Status = IsPlaying ? "正在自动阅读" : "已暂停"; _ = PersistAsync(); }

    private void Tick(object? sender, EventArgs e)
    {
        var seconds = _clock.Elapsed.TotalSeconds; _clock.Restart();
        // A blocked dispatcher, lock screen or resume cannot consume unread pages in a burst.
        Advance(seconds);
    }

    internal void Advance(double seconds)
    {
        if (!IsPlaying || CurrentPage is null || seconds <= 0 || seconds > 1) return;
        _sinceSave += seconds;
        if (Settings.Mode == "Scroll")
        {
            ScrollOffset += Math.Min(seconds, .1) * Settings.PixelsPerSecond;
            var distance = _scrollDistance;
            if (ScrollOffset >= distance)
            {
                if (FollowingPage is null) Finish(); else { var remainder = ScrollOffset - distance; Next(false); ScrollOffset = remainder; }
            }
            Frame?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _elapsed += seconds;
            if (_elapsed >= Settings.SecondsPerPage) { if (FollowingPage is null) Finish(); else Next(false); }
        }
        if (_sinceSave >= 4) { _sinceSave = 0; _ = PersistAsync(); }
    }

    private void Finish()
    {
        if (Settings.EndBehavior == "Loop" && IsPlaying) { var pauseOnManual = Settings.PauseOnManualNavigation; Settings.PauseOnManualNavigation = false; Seek(0); Settings.PauseOnManualNavigation = pauseOnManual; return; }
        PauseCore(); ScrollOffset = 0; _elapsed = 0; Status = "阅读完成，已保留末段";
        Frame?.Invoke(this, EventArgs.Empty); OnChanged(); _ = PersistAsync();
        if (Settings.EndBehavior == "Hide") HideRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Snapshot()
    {
        if (Document is null) return;
        var record = Settings.RecentFiles.FirstOrDefault(r => string.Equals(r.Path, Document.Path, StringComparison.OrdinalIgnoreCase));
        if (record is not null) record.Offset = Offset;
    }
    public async Task PersistAsync()
    {
        Snapshot();
        try { await _save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status = "阅读设置保存失败：" + ex.Message; OnChanged(); }
    }
    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    { if (e.Reason == SessionSwitchReason.SessionLock) _timer.Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) Pause(); })); }
    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Suspend) _timer.Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) Pause(); })); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; PauseCore(); Snapshot(); _timer.Tick -= Tick; _load?.Cancel(); _load?.Dispose();
        SystemEvents.SessionSwitch -= SessionSwitch; SystemEvents.PowerModeChanged -= PowerModeChanged;
    }
}
