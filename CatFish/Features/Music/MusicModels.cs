using System.Security.Cryptography;
using System.Text;

namespace CatFish.Features.Music;

public sealed class MusicSettings
{
    public string Source { get; set; } = "kugou";
    public string Style { get; set; } = "light";
    public int Count { get; set; } = 20;
    public double Volume { get; set; } = .5;
    public string Mode { get; set; } = "顺序播放";
    public string LocalFolder { get; set; } = "";
    public void Normalize()
    {
        LocalFolder ??= "";
        if (!MusicCatalog.Sources.Any(s => s.Id == Source)) Source = "kugou";
        if (!MusicCatalog.Styles.Any(s => s.Id == Style)) Style = "light";
        if (Count is not (10 or 20 or 30)) Count = 20;
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : .5;
        if (Mode is not ("顺序播放" or "列表循环" or "单曲循环" or "随机播放")) Mode = "顺序播放";
    }
}

public sealed record MusicSource(string Id, string Name, string Home)
{
    public override string ToString() => Name;
}
public sealed record MusicStyle(string Id, string Name, int KugouRank, string NeteaseQuery, string YitingRank)
{
    public override string ToString() => Name;
}
public sealed record MusicTrack(string Source, string Id, string Title, string Artist, string PageUrl, string AudioPath = "")
{
    public string Display => $"{Title}  ·  {Artist}";
}
public sealed record MusicRecommendation(IReadOnlyList<MusicTrack> Tracks, DateOnly Date, bool Cached, int FilteredCount = 0);
public interface IMusicCatalog
{
    Task<MusicRecommendation> RecommendAsync(string source, string style, int count, int batch, CancellationToken cancellationToken);
    Task<Uri> ResolveAsync(MusicTrack track, CancellationToken cancellationToken);
    void ExcludeTemporarily(MusicTrack track) { }
}
internal static class DailyMusic
{
    // Stable across launches, cultures and runtime versions; larger counts preserve the prefix.
    internal static IReadOnlyList<MusicTrack> Select(IEnumerable<MusicTrack> tracks, DateOnly date, string style, int count, int batch)
        => tracks.DistinctBy(t => (t.Source, t.Id))
            .OrderBy(t => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{date:yyyy-MM-dd}|{style}|{batch}|{t.Source}|{t.Id}"))), StringComparer.Ordinal)
            .Take(count).ToArray();
}
