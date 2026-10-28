using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace CatFish.Features.Music;

public sealed class MusicCatalog : IMusicCatalog, IDisposable
{
    public static readonly MusicSource[] Sources =
    [
        new("kugou", "酷狗音乐", "https://m.kugou.com/"),
        new("netease", "网易云音乐", "https://music.163.com/"),
        new("yiting", "一听音乐", "https://h5.1ting.com/"),
        new("local", "本地音乐", "")
    ];
    public static readonly MusicStyle[] Styles =
    [
        new("pop", "流行精选", 8888, "流行", "song_t"),
        new("light", "轻音乐 / 专注", 59900, "纯音乐", "light"),
        new("folk", "民谣 / 乡村", 51341, "民谣", "folk"),
        new("rock", "摇滚", 59896, "摇滚", "rock"),
        new("classic", "古典", 59899, "古典", "classic"),
        new("dance", "DJ / 舞曲", 24971, "舞曲", "dance")
    ];
    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<(string Source, string Id), DateTimeOffset> _excluded = new();
    public MusicCatalog(string? cacheDirectory = null, HttpMessageHandler? handler = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatFish", "Music");
        _http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 CatFish/1.0");
    }
    public async Task<MusicRecommendation> RecommendAsync(string source, string style, int count, int batch, CancellationToken cancellationToken)
    {
        if (!Sources.Any(s => s.Id == source)) throw new ArgumentException("未知音乐源");
        var category = Styles.First(s => s.Id == style);
        if (count is not (10 or 20 or 30)) throw new ArgumentOutOfRangeException(nameof(count));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "China Standard Time"));
        var path = Path.Combine(_cacheDirectory, $"{source}-{style}.json");
        CachedPool? cache = null;
        try { cache = JsonSerializer.Deserialize<CachedPool>(await File.ReadAllTextAsync(path, cancellationToken)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        if (cache?.Tracks is not { Count: > 0 } || cache.Tracks.Any(t => t is null || t.Source != source || string.IsNullOrWhiteSpace(t.Id))) cache = null;
        if (cache?.Date == today) return await FilterAsync(cache.Tracks, today, style, count, batch, true, cancellationToken);
        List<MusicTrack> pool;
        var poolDate = today;
        var cached = false;
        try
        {
            pool = source switch
            {
                "kugou" => await KugouAsync(category, cancellationToken),
                "netease" => await NeteaseAsync(category, cancellationToken),
                _ => await YitingAsync(category, cancellationToken)
            };
            cancellationToken.ThrowIfCancellationRequested();
            if (pool.Count == 0) throw new InvalidDataException("此风格暂无可用曲目，请切换风格或播放源。");
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new CachedPool(today, pool)), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporary, path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception ex) when (cache is not null && !cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        { pool = cache.Tracks; poolDate = cache.Date; cached = true; }
        // Cached files contain candidates, never proof of current playability.
        return await FilterAsync(pool, poolDate, style, count, batch, cached, cancellationToken);
    }
    internal async Task<MusicRecommendation> FilterAsync(IEnumerable<MusicTrack> pool, DateOnly date, string style, int count, int batch, bool cached, CancellationToken ct)
    {
        var ordered = DailyMusic.Select(pool, date, style, int.MaxValue, batch);
        var playable = new List<MusicTrack>();
        var filtered = 0;
        // Small batches preserve daily order and bound simultaneous requests. A slow source
        // must not leave the window loading indefinitely; only verified results are returned.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        foreach (var group in ordered.Chunk(4))
        {
            ct.ThrowIfCancellationRequested();
            if (budget.IsCancellationRequested) break;
            var results = await Task.WhenAll(group.Select(async track =>
            {
                if (_excluded.TryGetValue((track.Source, track.Id), out var until) && until > DateTimeOffset.UtcNow) return false;
                try { await ResolveAsync(track, budget.Token); return true; }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
                { return false; }
            }));
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < group.Length && playable.Count < count; i++)
            {
                if (results[i]) playable.Add(group[i]); else filtered++;
            }
            if (playable.Count >= count) break;
        }
        return new(playable, date, cached, filtered);
    }
    public void ExcludeTemporarily(MusicTrack track)
        => _excluded[(track.Source, track.Id)] = DateTimeOffset.UtcNow.AddMinutes(2);
    private async Task<List<MusicTrack>> KugouAsync(MusicStyle style, CancellationToken ct)
    {
        var tracks = new List<MusicTrack>();
        for (var page = 1; page <= 3; page++)
        {
            using var json = await JsonAsync($"https://m.kugou.com/rank/info/{style.KugouRank}?json=true&page={page}", ct);
            foreach (var item in json.RootElement.GetProperty("songs").GetProperty("list").EnumerateArray())
            {
                var id = Text(item, "hash"); var name = Text(item, "filename");
                var parts = name.Split(" - ", 2);
                if (id.Length > 0 && name.Length > 0)
                    tracks.Add(new("kugou", id, parts.Length > 1 ? parts[1] : name, parts.Length > 1 ? parts[0] : "酷狗音乐", $"https://www.kugou.com/song/#hash={id}"));
            }
        }
        return tracks;
    }
    private async Task<List<MusicTrack>> NeteaseAsync(MusicStyle style, CancellationToken ct)
    {
        using var search = await JsonAsync($"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(style.NeteaseQuery)}&type=1000&limit=30", ct);
        var tracks = new List<MusicTrack>();
        // Anonymous playlist responses may contain only ten tracks. Combine multiple matching lists.
        foreach (var list in search.RootElement.GetProperty("result").GetProperty("playlists").EnumerateArray().Take(30))
        {
            using var json = await JsonAsync($"https://music.163.com/api/playlist/detail?id={Text(list, "id")}", ct);
            foreach (var item in json.RootElement.GetProperty("result").GetProperty("tracks").EnumerateArray())
            {
                // fee=8 allows free standard-quality streaming; exclude VIP/purchase-only tracks.
                if (!item.TryGetProperty("fee", out var fee) || fee.GetInt32() is not (0 or 8)) continue;
                var id = Text(item, "id");
                tracks.Add(new("netease", id, Text(item, "name"), string.Join(" / ", item.GetProperty("artists").EnumerateArray().Select(a => Text(a, "name"))), $"https://music.163.com/#/song?id={id}"));
            }
            if (tracks.DistinctBy(t => t.Id).Count() >= 90) break;
        }
        return tracks;
    }
    private async Task<List<MusicTrack>> YitingAsync(MusicStyle style, CancellationToken ct)
        => ParseYiting(await _http.GetStringAsync($"https://h5.1ting.com/rank/{style.YitingRank}.html", ct));
    internal static List<MusicTrack> ParseYiting(string html)
    {
        var tracks = new List<MusicTrack>();
        foreach (Match match in Regex.Matches(html, "data-meta=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            using var json = JsonDocument.Parse(Uri.UnescapeDataString(WebUtility.HtmlDecode(match.Groups[1].Value)));
            var item = json.RootElement; var id = Text(item, "song_id");
            var path = Text(item, "song_filepath"); if (path.Length == 0) path = Text(item, "song_path");
            if (id.Length > 0 && path.StartsWith('/') && !path.StartsWith("//"))
                tracks.Add(new("yiting", id, Text(item, "song_name"), Text(item, "singer_name"), $"https://h5.1ting.com/song/{id}.html", path));
        }
        return tracks;
    }
    public async Task<Uri> ResolveAsync(MusicTrack track, CancellationToken cancellationToken)
    {
        if (track.Source == "local") return await LocalMusicLibrary.ResolveAsync(track, cancellationToken);
        string url;
        switch (track.Source)
        {
            case "kugou":
                using (var json = await JsonAsync($"https://m.kugou.com/app/i/getSongInfo.php?cmd=playInfo&hash={Uri.EscapeDataString(track.Id)}", cancellationToken))
                    url = Text(json.RootElement, "url");
                break;
            case "netease": url = $"https://music.163.com/song/media/outer/url?id={Uri.EscapeDataString(track.Id)}.mp3"; break;
            case "yiting": url = "https://h5.1ting.com/file?url=" + Uri.EscapeDataString(Regex.Replace(track.AudioPath, @"\.wma$", ".mp3", RegexOptions.IgnoreCase)); break;
            default: throw new InvalidDataException("未知播放源。");
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException("此曲暂无免登录试听地址。");
        for (var redirects = 0; redirects < 5; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new(0, 1023);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            // .NET deliberately leaves HTTPS -> HTTP redirects unresolved. These public CDNs
            // also support HTTPS, so upgrade the published Location and verify the actual audio.
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
            {
                uri = new Uri(response.RequestMessage?.RequestUri ?? uri, location);
                if (uri.Scheme == "http") uri = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
                if (uri.Scheme != "https") throw new InvalidDataException("音频重定向地址无效。");
                continue;
            }
            response.EnsureSuccessStatusCode();
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!(type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) || type == "application/octet-stream"))
                throw new InvalidDataException("音乐源未返回音频，可能已下架或需要登录。");
            using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bodyTimeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var stream = await response.Content.ReadAsStreamAsync(bodyTimeout.Token);
            var header = new byte[1024];
            var length = await stream.ReadAtLeastAsync(header, 16, false, bodyTimeout.Token);
            if (!IsAudioHeader(header.AsSpan(0, length)))
                throw new InvalidDataException("音乐源返回空文件或无效音频。");
            return response.RequestMessage!.RequestUri!;
        }
        throw new InvalidDataException("音频地址重定向过多，请更换音乐源。");
    }
    private static bool IsAudioHeader(ReadOnlySpan<byte> data)
        => data.Length >= 16 && (data.StartsWith("ID3"u8) || data.StartsWith("fLaC"u8)
            || data.StartsWith("OggS"u8) || (data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WAVE"u8))
            || data.Slice(4, 4).SequenceEqual("ftyp"u8)
            || (data[0] == 0xff && (data[1] & 0xe0) == 0xe0));
    private async Task<JsonDocument> JsonAsync(string url, CancellationToken ct)
    {
        var text = await _http.GetStringAsync(url, ct);
        try { return JsonDocument.Parse(text); }
        catch (JsonException) { throw new InvalidDataException("播放源返回了无法识别的数据，请切换其他源。"); }
    }
    private static string Text(JsonElement item, string property) => item.TryGetProperty(property, out var value) ? value.ToString() : "";
    public void Dispose() => _http.Dispose();
    public sealed record CachedPool(DateOnly Date, List<MusicTrack> Tracks);
}
