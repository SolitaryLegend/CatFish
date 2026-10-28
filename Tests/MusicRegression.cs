using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Reflection;
using CatFish.Core;
using CatFish.UI;
using CatFish.Features.Music;

internal static class MusicRegression
{
    internal static void Run(Action<bool, string> check, Action<Window, string> render, bool live)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var task = live ? LiveAsync(check, render) : OfflineAsync(check, render);
        Pump(task);
    }
    internal static void RunLocal(Action<bool, string> check, Action<Window, string> render, string folder)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Pump(LocalMusicRegression.PlaySamplesAsync(check, render, folder));
    }
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; }; timer.Start();
        Dispatcher.PushFrame(frame); timer.Stop(); task.GetAwaiter().GetResult();
    }
    private static MusicTrack[] Tracks(int count = 40) => Enumerable.Range(1, count)
        .Select(i => new MusicTrack("kugou", i.ToString(), $"午后钢琴 · 第 {i} 首", "独立音乐人", "https://m.kugou.com/")).ToArray();
    private static async Task OfflineAsync(Action<bool, string> check, Action<Window, string> render)
    {
        var date = new DateOnly(2026, 10, 23); var tracks = Tracks();
        var ten = DailyMusic.Select(tracks, date, "light", 10, 0);
        var thirty = DailyMusic.Select(tracks.Concat(tracks), date, "light", 30, 0);
        check(ten.Count == 10 && thirty.Count == 30 && thirty.Select(t => t.Id).Distinct().Count() == 30, "daily lists have requested counts and no duplicates");
        check(ten.SequenceEqual(thirty.Take(10)) && ten.SequenceEqual(DailyMusic.Select(tracks.Reverse(), date, "light", 10, 0)), "daily order survives restart, source ordering and count changes");
        check(!ten.SequenceEqual(DailyMusic.Select(tracks, date.AddDays(1), "light", 10, 0)) && !ten.SequenceEqual(DailyMusic.Select(tracks, date, "light", 10, 1)), "new day and another batch rotate recommendations");
        var settings = new MusicSettings { Source = "invalid", Style = "unknown", Count = 500, Volume = double.NaN, Mode = "bad" }; settings.Normalize();
        check(settings.Source == "kugou" && settings.Style == "light" && settings.Count == 20 && settings.Volume == .5 && settings.Mode == "顺序播放", "invalid persisted settings recover safely");
        var meta = Uri.EscapeDataString("""{"song_id":"123","song_name":"月光","singer_name":"钢琴","song_filepath":"/music/1.Wma"}""");
        var parsed = MusicCatalog.ParseYiting($"<li data-meta=\"{meta}\"></li>");
        check(parsed.Count == 1 && parsed[0].Title == "月光" && parsed[0].AudioPath == "/music/1.Wma", "one-listening parses actual encoded mobile metadata");

        var fake = new FakeCatalog(); var audio = new List<FakeAudio>();
        using var service = new MusicPlayerService(new(), fake, () => { var a = new FakeAudio(); audio.Add(a); return a; });
        await service.LoadAsync(); check(service.Queue.Count == 20 && !service.IsPlaying, "recommendations do not autoplay");
        await service.PlayAsync(0); check(service.IsPlaying && audio[^1].Playing, "play opens the selected track");
        var musicField = typeof(AppController).GetField("_music", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldMusic = musicField.GetValue(null); musicField.SetValue(null, service);
        try
        {
            var dispatch = typeof(AppController).GetMethod("HandleHotkey", BindingFlags.NonPublic | BindingFlags.Static)!;
            void Press(string action) => dispatch.Invoke(null, new object[] { action });
            Press("MusicPlayback"); check(!service.IsPlaying, "music playback shortcut pauses active audio");
            Press("MusicPlayback"); check(service.IsPlaying, "music playback shortcut resumes audio");
            Press("MusicNext"); check(service.Index == 1 && service.IsPlaying, "music next shortcut advances playback");
            Press("MusicPrevious"); check(service.Index == 0 && service.IsPlaying, "music previous shortcut returns to the previous track");
        }
        finally { musicField.SetValue(null, oldMusic); }
        var openedAudioCount = audio.Count;
        service.Pause(); check(!service.IsPlaying && !audio[^1].Playing, "pause preserves playback position");
        await service.ToggleAsync(); check(service.IsPlaying && audio.Count == openedAudioCount, "resume reuses the opened audio");
        service.Seek(80); check(service.Position.TotalSeconds == 80, "seek changes playback position");
        service.SetVolume(2); check(audio[^1].Volume == 1, "volume is clamped and applied");
        await service.PreviousAsync(); check(service.Index == 19 && audio[0].Disposed, "previous wraps and releases the old audio");
        service.Settings.Mode = "顺序播放"; audio[^1].End(); check(!service.IsPlaying, "sequential playback stops at the end");
        service.Settings.Mode = "单曲循环"; await service.PlayAsync(2); audio[^1].End(); await Task.Yield(); check(service.Index == 2 && service.IsPlaying, "single-track repeat reopens the same song");
        fake.FailAll = true; await service.PlayAsync(0);
        check(!service.IsPlaying && service.Index == -1 && service.Queue.Count == 15 && service.Queue[0].Id == "6" && service.LastFailure.Contains("移除"), "five failed tracks are removed and repeat mode stops without corrupting queue indices"); fake.FailAll = false;
        await service.PlayAsync(0); check(service.Current?.Id == "6" && service.IsPlaying, "playback resumes at the first remaining track after removal");
        fake.FailAll = true; await service.PlayAsync(service.Queue.Count - 1);
        check(!service.IsPlaying && service.Queue.Count == 14 && service.Index == -1, "removing the last track leaves no invalid selection"); fake.FailAll = false;
        fake.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = service.PlayAsync(0); service.Stop(); fake.Block.SetResult(new Uri("https://audio.example/late.mp3")); await pending;
        check(!service.IsPlaying && !service.IsOpening, "stop cancels a pending resolution and late results cannot autoplay"); fake.Block = null;
        fake.LoadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var older = service.LoadAsync(); fake.LoadGate = null;
        service.Settings.Count = 10; await service.LoadAsync(); fake.LastGate!.SetResult(new(Tracks(30), date, false)); await older;
        check(service.Queue.Count == 10 && !service.IsLoading, "late old recommendations cannot replace the latest source request");
        fake.FailLoad = true; await service.LoadAsync(); check(service.Queue.Count == 10 && service.Status.Contains("保留"), "network failures preserve the existing queue"); fake.FailLoad = false;
        service.SetSleep(15); check(service.StopAt > DateTimeOffset.Now, "sleep timer is scheduled"); service.SetSleep(0); check(service.StopAt is null, "sleep timer can be cancelled");
        service.Settings.Volume = .5; service.Settings.Count = 20; await service.LoadAsync(); await service.PlayAsync(0);
        var window = new MusicPlayerWindow(service);
        fake.LoadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadingPreview = service.LoadAsync();
        render(window, "music-loading.png");
        fake.LastGate!.SetResult(new(Tracks(20), DateOnly.FromDateTime(DateTime.Today), false)); fake.LoadGate = null;
        await loadingPreview; await service.PlayAsync(0);
        render(window, "music-player.png"); window.Close();
        check(service.IsPlaying, "closing the player window retains background playback");
        var main = new MainWindow();
        render(main, "main-music.png");
        using (var tray = new TrayService(main))
        {
            var menu = (System.Windows.Forms.ContextMenuStrip)typeof(TrayService).GetField("_menu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tray)!;
            check(menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Any(i => i.Text == "音乐播放"), "tray exposes the music player entry");
            var controls = menu.Items.Cast<System.Windows.Forms.ToolStripItem>().OfType<System.Windows.Forms.ToolStripMenuItem>().Single(i => i.Text == "音乐播放");
            check(controls.DropDownItems.Cast<System.Windows.Forms.ToolStripItem>().Select(i => i.Text)
                .SequenceEqual(new[] { "程序设置", "播放 / 暂停", "上一首", "下一首", "停止" }), "tray groups music settings with playback, previous, next and stop controls");
        }
        // The launcher was never shown and must not initialize global hooks during this test.
        main.ExitRequested = true; main.Close();
        await RedirectAsync(check);
        await FilteringAsync(check);
        await CacheAsync(check);
        await LocalMusicRegression.RunAsync(check, render);
    }
    private static async Task FilteringAsync(Action<bool, string> check)
    {
        var handler = new CandidateHttp(); using var catalog = new MusicCatalog(handler: handler);
        var date = new DateOnly(2026, 10, 27);
        var candidates = Tracks(90);
        var expected = DailyMusic.Select(candidates, date, "light", 90, 0).Where(t => int.Parse(t.Id) % 8 >= 4).Take(30).ToArray();
        var result = await catalog.FilterAsync(candidates, date, "light", 30, 0, false, default);
        check(result.Tracks.SequenceEqual(expected) && result.FilteredCount > 0, "preflight removes missing URLs, HTML, empty files and invalid audio, filling from later candidates in stable order");
        var ten = await catalog.FilterAsync(candidates, date, "light", 10, 0, false, default);
        check(ten.Tracks.SequenceEqual(result.Tracks.Take(10)), "filtered recommendations retain the count prefix");
        catalog.ExcludeTemporarily(result.Tracks[0]);
        var afterFailure = await catalog.FilterAsync(candidates, date, "light", 10, 0, false, default);
        check(!afterFailure.Tracks.Contains(result.Tracks[0]) && afterFailure.Tracks.Count == 10, "runtime decoder failures stay excluded during immediate refresh");
        var shortList = await catalog.FilterAsync(candidates.Take(8), date, "light", 30, 0, false, default);
        check(shortList.Tracks.Count <= 4 && shortList.Tracks.All(t => int.Parse(t.Id) % 8 >= 4), "short lists contain only verified tracks instead of padding with failures");
        using var cancelled = new CancellationTokenSource();
        handler.Delay = true;
        var pending = catalog.FilterAsync(candidates, date, "light", 30, 0, false, cancelled.Token);
        cancelled.Cancel();
        var didCancel = false; try { await pending; } catch (OperationCanceledException) { didCancel = true; }
        check(didCancel, "cancelled preflight does not return a partial stale list");
        using var emptyService = new MusicPlayerService(new(), new EmptyCatalog(), () => new FakeAudio());
        await emptyService.LoadAsync();
        check(emptyService.Queue.Count == 0 && emptyService.Status.Contains("未找到可播放"), "fully filtered lists show an explicit empty state");
    }
    private static async Task RedirectAsync(Action<bool, string> check)
    {
        var handler = new RedirectHttp(); using var catalog = new MusicCatalog(handler: handler);
        var uri = await catalog.ResolveAsync(new("netease", "123", "测试", "作者", "https://music.163.com/"), default);
        check(uri.Scheme == "https" && uri.Host == "cdn.example" && handler.Calls == 2 && handler.SawRange, "public audio redirects upgrade to HTTPS and validate a ranged audio response");
        handler.Html = true;
        var rejected = false; try { await catalog.ResolveAsync(new("netease", "123", "测试", "作者", "https://music.163.com/"), default); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "login or error HTML is rejected instead of sent to the decoder");
    }
    private static async Task CacheAsync(Action<bool, string> check)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "music-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "China Standard Time")).AddDays(-1);
            await File.WriteAllTextAsync(Path.Combine(dir, "kugou-light.json"), JsonSerializer.Serialize(new MusicCatalog.CachedPool(yesterday, Tracks().ToList())));
            using var catalog = new MusicCatalog(dir, new FailingHttp());
            var result = await catalog.RecommendAsync("kugou", "light", 30, 0, default);
            check(result.Cached && result.Date == yesterday && result.Tracks.Count == 0, "offline fallback cannot display unverified cached tracks");
            var today = yesterday.AddDays(1);
            await File.WriteAllTextAsync(Path.Combine(dir, "kugou-light.json"), JsonSerializer.Serialize(new MusicCatalog.CachedPool(today, Tracks(90).ToList())));
            using var liveCache = new MusicCatalog(dir, new CandidateHttp());
            var verified = await liveCache.RecommendAsync("kugou", "light", 30, 0, default);
            check(verified.Cached && verified.Tracks.Count == 30 && verified.Tracks.All(t => int.Parse(t.Id) % 8 >= 4), "same-day legacy cache is also filtered before display");
            using var cts = new CancellationTokenSource(); cts.Cancel();
            var cancelled = false; try { await catalog.RecommendAsync("kugou", "light", 10, 0, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "cancellation cannot silently return cached recommendations");
        }
        finally { Directory.Delete(dir, true); }
    }
    private static async Task LiveAsync(Action<bool, string> check, Action<Window, string> render)
    {
        var cachePath = Path.Combine(AppContext.BaseDirectory, "music-live-cache-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
        using var catalog = new MusicCatalog(cachePath);
        foreach (var source in MusicCatalog.Sources.Where(s => s.Id != "local"))
        {
            foreach (var style in MusicCatalog.Styles.Where(s => s.Id == "light"))
            {
                var result = await catalog.RecommendAsync(source.Id, style.Id, 30, 0, default);
                check(result.Tracks.Count > 0 && result.Tracks.Count <= 30 && result.Tracks.All(t => t.Source == source.Id)
                    && result.Tracks.DistinctBy(t => t.Id).Count() == result.Tracks.Count,
                    $"live {source.Name}/{style.Name}: {result.Tracks.Count} audio-verified recommendations, {result.FilteredCount} rejected");
            }
            using var service = new MusicPlayerService(new() { Source = source.Id, Style = "light", Volume = 0 }, new MusicCatalog(cachePath), () => new WpfMusicAudio());
            await service.LoadAsync(); var first = service.Queue.FirstOrDefault(); await service.PlayAsync(0); await Task.Delay(1500);
            Console.WriteLine($"PLAYBACK {source.Name}: {service.Status} / {service.LastFailure}");
            check(service.IsPlaying && service.Current == first && service.Duration.TotalSeconds > 0 && service.Position.TotalSeconds > 0, $"live {source.Name}: selected first track plays without skipping ({service.Current?.Title}, {service.Duration.TotalSeconds:F0}s)");
            var window = new MusicPlayerWindow(service); render(window, $"music-live-{source.Id}.png"); window.Close();
        }
    }
    private sealed class FakeCatalog : IMusicCatalog
    {
        public bool FailAll, FailLoad;
        public TaskCompletionSource<Uri>? Block;
        public TaskCompletionSource<MusicRecommendation>? LoadGate, LastGate;
        public Task<MusicRecommendation> RecommendAsync(string source, string style, int count, int batch, CancellationToken ct)
        {
            if (LoadGate is { } gate) { LastGate = gate; return gate.Task; }
            if (FailLoad) throw new HttpRequestException();
            return Task.FromResult(new MusicRecommendation(Tracks(count), DateOnly.FromDateTime(DateTime.Today), false));
        }
        public Task<Uri> ResolveAsync(MusicTrack track, CancellationToken ct)
        {
            if (Block is not null) return Block.Task;
            if (FailAll) throw new InvalidDataException("此曲暂无免登录试听地址。");
            return Task.FromResult(new Uri("https://audio.example/test.mp3"));
        }
    }
    private sealed class FakeAudio : IMusicAudio
    {
        public event EventHandler? Ended;
        public event EventHandler? Failed { add { } remove { } }
        public double Volume { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromMinutes(3);
        public bool Playing, Disposed;
        public Task OpenAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
        public void Play() => Playing = true;
        public void Pause() => Playing = false;
        public void Dispose() { Disposed = true; Playing = false; }
        public void End() => Ended?.Invoke(this, EventArgs.Empty);
    }
    private sealed class FailingHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
    private sealed class EmptyCatalog : IMusicCatalog
    {
        public Task<MusicRecommendation> RecommendAsync(string source, string style, int count, int batch, CancellationToken ct)
            => Task.FromResult(new MusicRecommendation([], DateOnly.FromDateTime(DateTime.Today), false, 30));
        public Task<Uri> ResolveAsync(MusicTrack track, CancellationToken ct) => throw new InvalidOperationException();
    }
    private static byte[] AudioHeader() => [73, 68, 51, 4, 0, 0, 0, 0, 0, 0, 255, 251, 144, 0, 0, 0, 0, 0, 0, 0];
    private sealed class CandidateHttp : HttpMessageHandler
    {
        public bool Delay;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Delay) await Task.Delay(5000, cancellationToken);
            var uri = request.RequestUri!;
            var isMetadata = uri.Host == "m.kugou.com";
            var id = int.Parse(isMetadata ? uri.Query.Split("hash=")[1] : uri.AbsolutePath.Trim('/'));
            var kind = id % 8;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            if (isMetadata)
            {
                response.Content = new StringContent(JsonSerializer.Serialize(new { url = kind == 0 ? "" : $"https://cdn.example/{id}" }));
            }
            else
            {
                response.Content = new ByteArrayContent(kind switch { 1 => "<html>login required</html>"u8.ToArray(), 2 => [], 3 => "<html>not audio at all</html>"u8.ToArray(), _ => AudioHeader() });
                response.Content.Headers.ContentType = new(kind == 1 ? "text/html" : "audio/mpeg");
            }
            return response;
        }
    }
    private sealed class RedirectHttp : HttpMessageHandler
    {
        public int Calls;
        public bool SawRange, Html;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.RequestUri!.Host == "music.163.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
                redirect.Headers.Location = new Uri("http://cdn.example/music.mp3"); return Task.FromResult(redirect);
            }
            SawRange = request.Headers.Range is not null;
            var result = new HttpResponseMessage(HttpStatusCode.PartialContent) { RequestMessage = request, Content = new ByteArrayContent(AudioHeader()) };
            result.Content.Headers.ContentType = new(Html ? "text/html" : "audio/mpeg"); return Task.FromResult(result);
        }
    }
}
