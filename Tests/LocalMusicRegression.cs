using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using CatFish.Core;
using CatFish.Features.Music;
using CatFish.UI;

internal static class LocalMusicRegression
{
    internal static async Task RunAsync(Action<bool, string> check, Action<Window, string> render)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "local-music-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "子目录"));
        try
        {
            foreach (var ext in LocalMusicLibrary.Extensions)
                await File.WriteAllBytesAsync(Path.Combine(dir, "子目录", "中文 # % 音乐" + ext.ToUpperInvariant()), Header(ext));
            for (var i = 0; i < 32; i++) await File.WriteAllBytesAsync(Path.Combine(dir, $"曲目{i:00}.mp3"), Header(".mp3"));
            await File.WriteAllTextAsync(Path.Combine(dir, "不是音乐.mp3"), "<html>invalid music file</html>");
            await File.WriteAllBytesAsync(Path.Combine(dir, "空文件.wav"), []);
            await File.WriteAllTextAsync(Path.Combine(dir, "说明.txt"), "ignore");
            var result = await LocalMusicLibrary.LoadAsync(dir, default);
            check(result.Tracks.Count == 40 && result.FilteredCount == 2, "local import includes nested folders and eight audio extensions, excluding invalid files");
            check(result.Tracks.All(t => t.Source == "local") && result.Tracks.Any(t => t.AudioPath.Contains("子目录")), "local tracks retain absolute paths and source identity");
            var track = result.Tracks.First(t => t.AudioPath.EndsWith(".MP3"));
            var uri = await LocalMusicLibrary.ResolveAsync(track, default);
            check(uri.IsFile && uri.LocalPath == track.AudioPath, "file URIs preserve Chinese, spaces, percent and hash characters");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var didCancel = false; try { await LocalMusicLibrary.LoadAsync(dir, cancelled.Token); } catch (OperationCanceledException) { didCancel = true; }
            check(didCancel, "folder scanning observes cancellation");
            using var player = new MusicPlayerService(new() { Source = "local", LocalFolder = dir, Count = 10 }, new MusicCatalog(handler: new NoNetwork()), () => new FakeAudio());
            await player.LoadAsync();
            check(player.Queue.Count == 40 && !player.IsPlaying && player.RecommendationLabel.Contains("本地音乐"), "local folders load all songs rather than the daily online limit and never autoplay");
            var saved = JsonSerializer.Deserialize<MusicSettings>(JsonSerializer.Serialize(player.Settings))!; saved.Normalize();
            check(saved.Source == "local" && saved.LocalFolder == dir, "local source and chosen folder survive settings serialization");
            var main = new MainWindow();
            using (var tray = new TrayService(main, player))
            {
                var icon = (System.Windows.Forms.NotifyIcon)typeof(TrayService).GetField("_icon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tray)!;
                check(icon.Text.Contains("双击打开主窗口"), "stopped music retains the default tray tooltip");
                await player.PlayAsync(0);
                check(icon.Text == "音乐播放中 · " + player.Current!.Title, "tray tooltip updates immediately when music plays");
                player.Pause();
                check(player.IsPaused && icon.Text == "音乐暂停中 · " + player.Current!.Title, "pause has a distinct tray state");
                await player.NextAsync();
                check(icon.Text == "音乐播放中 · " + player.Current!.Title, "track changes update the tray title");
                player.Stop();
                check(!player.IsPaused && icon.Text.Contains("双击打开主窗口"), "stop clears the music tray tooltip even when Current remains selected");
            }
            player.Settings.LocalFolder = Path.Combine(dir, "missing"); await player.LoadAsync();
            check(player.Queue.Count == 40 && player.Status.Contains("读取失败"), "a missing folder retains the existing playback queue");
            player.Settings.LocalFolder = dir; await player.LoadAsync();
            var window = new MusicPlayerWindow(player); render(window, "music-local.png"); window.Close();
            var removed = player.Queue[0]; File.Delete(removed.AudioPath); await player.PlayAsync(0);
            check(!player.Queue.Contains(removed) && player.IsPlaying, "a locally deleted file is removed without deleting other files or breaking the next song");
            using var longTitlePlayer = new MusicPlayerService(new(), new LongTitleCatalog(), () => new FakeAudio());
            await longTitlePlayer.LoadAsync(); await longTitlePlayer.PlayAsync(0);
            var tooltip = TrayService.MusicTooltip(longTitlePlayer);
            check(tooltip.Length <= 63 && tooltip.EndsWith('…') && !tooltip.Contains('\n') && !tooltip.Contains('\ufffd'), "long Unicode titles are safely shortened for the tray tooltip");
        }
        finally { Directory.Delete(dir, true); }
    }

    internal static async Task PlaySamplesAsync(Action<bool, string> check, Action<Window, string> render, string folder)
    {
        using var service = new MusicPlayerService(new() { Source = "local", LocalFolder = Path.GetFullPath(folder), Volume = 0 }, new MusicCatalog(handler: new NoNetwork()), () => new WpfMusicAudio());
        await service.LoadAsync();
        check(service.Queue.Count >= 3, "downloaded music folder imports at least three tracks without network access");
        var failures = new List<string>();
        foreach (var track in service.Queue.ToArray())
        {
            var index = service.Queue.ToList().IndexOf(track);
            if (index < 0) { failures.Add(track.Title); continue; }
            await service.PlayAsync(index); await Task.Delay(1000);
            var good = service.Current == track && service.IsPlaying && service.Duration.TotalSeconds > 0 && service.Position.TotalSeconds > 0;
            Console.WriteLine($"LOCAL {(good ? "PASS" : "FAIL")}: {Path.GetFileName(track.AudioPath)} · {service.Duration.TotalSeconds:F1}s · {service.LastFailure}");
            if (!good) failures.Add(Path.GetFileName(track.AudioPath));
        }
        var window = new MusicPlayerWindow(service); render(window, "music-local-samples.png"); window.Close();
        check(failures.Count == 0, "all local samples decode and advance with volume zero: " + string.Join(", ", failures));
    }
    private static byte[] Header(string extension)
    {
        var data = new byte[32];
        switch (extension)
        {
            case ".wav": Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0); Encoding.ASCII.GetBytes("WAVE").CopyTo(data, 8); break;
            case ".aif": case ".aiff": Encoding.ASCII.GetBytes("FORM").CopyTo(data, 0); Encoding.ASCII.GetBytes("AIFF").CopyTo(data, 8); break;
            case ".wma": new byte[] { 0x30, 0x26, 0xb2, 0x75, 0x8e, 0x66, 0xcf, 0x11 }.CopyTo(data, 0); break;
            case ".m4a": Encoding.ASCII.GetBytes("ftyp").CopyTo(data, 4); break;
            case ".flac": Encoding.ASCII.GetBytes("fLaC").CopyTo(data, 0); break;
            case ".aac": data[0] = 0xff; data[1] = 0xf1; break;
            default: Encoding.ASCII.GetBytes("ID3").CopyTo(data, 0); break;
        }
        return data;
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("Local playback must never use HTTP");
    }
    private sealed class FakeAudio : IMusicAudio
    {
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler? Failed { add { } remove { } }
        public double Volume { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromMinutes(1);
        public Task OpenAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void Dispose() { }
    }
    private sealed class LongTitleCatalog : IMusicCatalog
    {
        public Task<MusicRecommendation> RecommendAsync(string source, string style, int count, int batch, CancellationToken ct)
            => Task.FromResult(new MusicRecommendation([new("kugou", "test", string.Concat(Enumerable.Repeat("乐🎵e\u0301", 30)) + "\n结尾", "", "")], DateOnly.FromDateTime(DateTime.Today), false));
        public Task<Uri> ResolveAsync(MusicTrack track, CancellationToken ct) => Task.FromResult(new Uri("https://example.test/music"));
    }
}
