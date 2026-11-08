using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;

namespace CatFish.Features.Music;

internal interface IMusicAudio : IDisposable
{
    event EventHandler? Ended;
    event EventHandler? Failed;
    double Volume { get; set; }
    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }
    Task OpenAsync(Uri uri, CancellationToken ct);
    void Play();
    void Pause();
}

internal sealed class WpfMusicAudio : IMusicAudio
{
    private readonly MediaPlayer _player = new();
    private readonly TaskCompletionSource _opened = new();
    public event EventHandler? Ended;
    public event EventHandler? Failed;
    public WpfMusicAudio()
    {
        _player.MediaOpened += (_, _) => _opened.TrySetResult();
        _player.MediaEnded += (_, _) => Ended?.Invoke(this, EventArgs.Empty);
        _player.MediaFailed += (_, e) =>
        {
            if (!_opened.TrySetException(new InvalidDataException("无法解码或连接此曲音频。", e.ErrorException)))
                Failed?.Invoke(this, EventArgs.Empty);
        };
    }
    public double Volume { get => _player.Volume; set => _player.Volume = value; }
    public TimeSpan Position { get => _player.Position; set => _player.Position = value; }
    public TimeSpan Duration => _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : TimeSpan.Zero;
    public async Task OpenAsync(Uri uri, CancellationToken ct)
    {
        _player.Open(uri);
        await _opened.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
    }
    public void Play() => _player.Play();
    public void Pause() => _player.Pause();
    public void Dispose() => _player.Close();
}

public sealed class MusicPlayerService : IDisposable
{
    private readonly IMusicCatalog _catalog;
    private readonly Func<IMusicAudio> _audioFactory;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _load;
    private CancellationTokenSource? _play;
    private IMusicAudio? _audio;
    private bool _disposed;
    private int _consecutiveFailures;
    private string _recommendationPrefix = "每日推荐";
    public MusicSettings Settings { get; }
    public IReadOnlyList<MusicTrack> Queue { get; private set; } = [];
    public int Index { get; private set; } = -1;
    public MusicTrack? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public bool IsPlaying { get; private set; }
    public bool IsPaused => _audio is not null && !IsPlaying && !IsOpening;
    public bool IsOpening { get; private set; }
    public bool IsLoading { get; private set; }
    public string Status { get; private set; } = "选择风格，发现今天的音乐。";
    public string RecommendationLabel { get; private set; } = "每日推荐";
    public DateOnly? RecommendationDate { get; private set; }
    public string LastFailure { get; private set; } = "";
    public DateTimeOffset? StopAt { get; private set; }
    public TimeSpan Position => _audio?.Position ?? TimeSpan.Zero;
    public TimeSpan Duration => _audio?.Duration ?? TimeSpan.Zero;
    public event EventHandler? Changed;
    public MusicPlayerService(MusicSettings settings) : this(settings, new MusicCatalog(), () => new WpfMusicAudio()) { }
    internal MusicPlayerService(MusicSettings settings, IMusicCatalog catalog, Func<IMusicAudio> audioFactory)
    {
        Settings = settings; settings.Normalize(); _catalog = catalog; _audioFactory = audioFactory;
        _timer.Tick += (_, _) =>
        {
            if (StopAt is { } end && DateTimeOffset.Now >= end)
            { StopAt = null; Stop(); Status = "已按定时设置停止播放。"; Notify(); }
        };
        _timer.Start();
    }
    public async Task LoadAsync(int batch = 0)
    {
        if (_disposed) return;
        _load?.Cancel();
        using var operation = new CancellationTokenSource(); _load = operation;
        var source = Settings.Source; var style = Settings.Style; var count = Settings.Count;
        var folder = Settings.LocalFolder;
        IsLoading = true; Status = source == "local" ? "正在读取音乐文件夹，请稍候…" : "正在筛选可播放歌曲，请稍候…"; Notify();
        try
        {
            var result = source == "local" ? await LocalMusicLibrary.LoadAsync(folder, operation.Token)
                : await _catalog.RecommendAsync(source, style, count, batch, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            Stop(); Queue = result.Tracks; Index = -1;
            LastFailure = result.FilteredCount > 0 ? $"已过滤 {result.FilteredCount} 首无法播放或暂时无法验证的歌曲。" : "";
            RecommendationDate = result.Date;
            _recommendationPrefix = source == "local" ? "本地音乐" : $"{result.Date:MM月dd日} · {MusicCatalog.Sources.First(s => s.Id == source).Name} · {MusicCatalog.Styles.First(s => s.Id == style).Name}";
            UpdateRecommendationLabel();
            if (source == "local")
            {
                Status = string.IsNullOrWhiteSpace(folder) ? "请选择音乐文件夹。"
                    : Queue.Count == 0 ? "文件夹内未找到支持的音乐文件。"
                    : $"已载入 {Queue.Count} 首本地音乐（包含子文件夹）。";
                return;
            }
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "China Standard Time"));
            Status = Queue.Count == 0 ? "未找到可播放歌曲，请检查网络或更换音乐源。"
                : Queue.Count < count ? $"本次确认可播放 {Queue.Count} 首，不足 {count} 首；可换源或换一批。"
                : result.Date != today ? $"使用 {result.Date:MM-dd} 缓存歌单，{Queue.Count} 首已重新验证。"
                : $"{Queue.Count} 首已验证可播放" + (result.FilteredCount > 0 ? $"，已过滤 {result.FilteredCount} 首。" : "，双击播放。");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) when (IsSourceError(ex))
        { if (!operation.IsCancellationRequested) Status = source == "local" ? "文件夹读取失败，保留原歌单。请重新选择文件夹。" : "获取失败，保留原歌单。请重试或换源。"; }
        finally
        {
            if (ReferenceEquals(_load, operation)) { _load = null; IsLoading = false; Notify(); }
        }
    }
    public async Task PlayAsync(int index)
    {
        _consecutiveFailures = 0;
        await PlayCoreAsync(index);
    }
    private async Task PlayCoreAsync(int index)
    {
        if (_disposed || index < 0 || index >= Queue.Count) return;
        CancelPlayback();
        using var operation = new CancellationTokenSource(); _play = operation;
        Index = index; IsOpening = true; Status = "正在连接：" + Current!.Title; Notify();
        try
        {
            var uri = await _catalog.ResolveAsync(Current!, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            var audio = _audioFactory(); _audio = audio; audio.Volume = Settings.Volume;
            await audio.OpenAsync(uri, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            audio.Ended += (_, _) => { if (ReferenceEquals(_audio, audio)) _ = OnEndedAsync(); };
            audio.Failed += (_, _) => { if (ReferenceEquals(_audio, audio)) _ = SkipFailedAsync("播放中断"); };
            IsOpening = false; IsPlaying = true; audio.Play();
            Status = "正在播放：" + Current!.Display; Notify();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) when (IsSourceError(ex) || ex is TimeoutException or InvalidOperationException or NotSupportedException)
        {
            if (!operation.IsCancellationRequested) await SkipFailedAsync(ex.Message);
        }
        finally { if (ReferenceEquals(_play, operation)) _play = null; }
    }
    private async Task SkipFailedAsync(string reason)
    {
        var failed = Current;
        if (failed is null) return;
        LastFailure = $"已移除《{failed.Title}》：{reason.TrimEnd('。', '.')}。";
        var next = Index;
        CancelPlayback();
        _catalog.ExcludeTemporarily(failed);
        Queue = Queue.Where((_, index) => index != next).ToArray();
        Index = -1; UpdateRecommendationLabel();
        if (++_consecutiveFailures >= 5 || next >= Queue.Count)
        { Status = Queue.Count == 0 ? "不可播放歌曲已移除，歌单为空，请换源或换一批。" : "失效歌曲已移除；连续失败或已到歌单末尾，请重试或换源。"; Notify(); return; }
        await PlayCoreAsync(next);
    }
    private void UpdateRecommendationLabel() => RecommendationLabel = $"{_recommendationPrefix} · {Queue.Count} 首";
    private async Task OnEndedAsync()
    {
        _consecutiveFailures = 0;
        if (Settings.Mode == "单曲循环") { await PlayCoreAsync(Index); return; }
        if (Settings.Mode == "顺序播放" && Index == Queue.Count - 1)
        { Stop(); Status = "本次推荐已播放完。"; Notify(); return; }
        await NextAsync();
    }
    public async Task ToggleAsync()
    {
        if (IsPlaying || IsOpening) { Pause(); return; }
        if (_audio is not null) { _audio.Play(); IsPlaying = true; Status = "正在播放：" + Current?.Display; Notify(); }
        else await PlayAsync(Index >= 0 ? Index : 0);
    }
    public Task NextAsync()
    {
        if (Queue.Count == 0) return Task.CompletedTask;
        var next = (Index + 1) % Queue.Count;
        if (Settings.Mode == "随机播放" && Queue.Count > 1 && Index >= 0)
            next = (Index + 1 + Random.Shared.Next(Queue.Count - 1)) % Queue.Count;
        return PlayAsync(next);
    }
    public Task PreviousAsync() => PlayAsync(Index <= 0 ? Queue.Count - 1 : Index - 1);
    public void Pause()
    {
        if (IsOpening) CancelPlayback(); else _audio?.Pause();
        IsPlaying = false; Status = "已暂停。"; Notify();
    }
    public void Stop() { CancelPlayback(); Status = "已停止。"; Notify(); }
    public void EmergencyStop()
    {
        _load?.Cancel(); IsLoading = false; StopAt = null; Stop();
    }
    public void Seek(double seconds)
    {
        if (_audio is not null && double.IsFinite(seconds) && Duration > TimeSpan.Zero)
            _audio.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Duration.TotalSeconds));
    }
    public void SetVolume(double value)
    {
        Settings.Volume = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : .5;
        if (_audio is not null) _audio.Volume = Settings.Volume;
    }
    public void SetSleep(int minutes) { StopAt = minutes > 0 ? DateTimeOffset.Now.AddMinutes(minutes) : null; Notify(); }
    private void CancelPlayback()
    {
        _play?.Cancel(); _play = null;
        _audio?.Dispose(); _audio = null; IsPlaying = false; IsOpening = false;
    }
    private static bool IsSourceError(Exception ex) => ex is HttpRequestException or IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or JsonException or OperationCanceledException or KeyNotFoundException or FormatException or InvalidOperationException or System.Text.RegularExpressions.RegexMatchTimeoutException;
    private void Notify() { if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }
    public void Dispose()
    {
        _disposed = true; _load?.Cancel(); CancelPlayback(); _timer.Stop();
        if (_catalog is IDisposable disposable) disposable.Dispose();
    }
}
