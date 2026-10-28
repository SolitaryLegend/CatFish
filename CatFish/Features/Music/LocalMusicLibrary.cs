using System.IO;

namespace CatFish.Features.Music;

internal static class LocalMusicLibrary
{
    internal static readonly string[] Extensions = [".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".aif", ".aiff"];
    internal const string FormatDescription = "MP3、WAV、WMA、M4A、AAC、FLAC、AIFF（实际解码取决于系统支持）";

    internal static Task<MusicRecommendation> LoadAsync(string folder, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(folder))
            return new MusicRecommendation([], DateOnly.FromDateTime(DateTime.Today), false);
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("所选音乐文件夹不存在或无法访问。");
        var tracks = new List<MusicTrack>();
        var skipped = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(file);
            if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) continue;
            try
            {
                ValidateFile(file);
                tracks.Add(new("local", file, Path.GetFileNameWithoutExtension(file), extension[1..].ToUpperInvariant(), "", file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { skipped++; }
        }
        return new MusicRecommendation(tracks.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToArray(), DateOnly.FromDateTime(DateTime.Today), false, skipped);
    }, ct);

    internal static Task<Uri> ResolveAsync(MusicTrack track, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(track.AudioPath)) throw new InvalidDataException("本地音乐路径无效。");
        var path = Path.GetFullPath(track.AudioPath);
        ValidateFile(path);
        ct.ThrowIfCancellationRequested();
        return new Uri(path, UriKind.Absolute);
    }, ct);

    private static void ValidateFile(string path)
    {
        if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("暂不支持此音乐格式。");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[16];
        var read = stream.ReadAtLeast(header, header.Length, false);
        if (read < 16 || !(header.StartsWith("ID3"u8) || header.StartsWith("fLaC"u8)
            || (header.StartsWith("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WAVE"u8))
            || (header.StartsWith("FORM"u8) && (header.Slice(8, 4).SequenceEqual("AIFF"u8) || header.Slice(8, 4).SequenceEqual("AIFC"u8)))
            || header.Slice(4, 4).SequenceEqual("ftyp"u8)
            || header.StartsWith(new byte[] { 0x30, 0x26, 0xb2, 0x75, 0x8e, 0x66, 0xcf, 0x11 })
            || (header[0] == 0xff && (header[1] & 0xe0) == 0xe0)))
            throw new InvalidDataException("文件为空、已损坏或不是支持的音频文件。");
    }
}
