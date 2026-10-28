using System.Globalization;
using System.IO;
using System.Text;

namespace CatFish.Features.Reader;

public sealed record TxtPage(int Start, int End, string Text, int ElementCount);

/// <summary>One bounded document and a Unicode offset index. Visible pages are built on demand.</summary>
public sealed class TxtDocument
{
    public const long MaximumBytes = 32 * 1024 * 1024;
    public const long LargeFileBytes = 20 * 1024 * 1024;
    private readonly int[] _elements;
    public string Path { get; }
    public string Text { get; }
    public string EncodingName { get; }
    public long FileSize { get; }
    public DateTime LastWriteUtc { get; }
    public int TotalElements => _elements.Length;
    public bool IsLarge => FileSize > LargeFileBytes;

    internal TxtDocument(string path, string text, string encodingName, long size, DateTime modified)
    {
        Path = path; Text = text; EncodingName = encodingName; FileSize = size; LastWriteUtc = modified;
        _elements = StringInfo.ParseCombiningCharacters(text);
    }

    public static Task<TxtDocument> LoadAsync(string path, string encoding = "Auto", CancellationToken token = default) => Task.Run(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!string.Equals(System.IO.Path.GetExtension(fullPath), ".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择 .txt 文本文件。");
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("文件超过 32 MB 阅读上限，请先拆分文本。");
        if (stream.Length == 0) throw new InvalidDataException("文件内容为空。");
        var modified = File.GetLastWriteTimeUtc(fullPath);
        using var buffer = new MemoryStream((int)stream.Length);
        var chunk = new byte[64 * 1024];
        int count;
        while ((count = stream.Read(chunk)) != 0)
        {
            token.ThrowIfCancellationRequested();
            if (buffer.Length + count > MaximumBytes) throw new InvalidDataException("文件超过 32 MB 阅读上限，请先拆分文本。");
            buffer.Write(chunk, 0, count);
        }
        var bytes = buffer.ToArray();
        var (codec, preamble) = DetectEncoding(bytes, encoding);
        var text = codec.GetString(bytes, preamble, bytes.Length - preamble);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("文件内容为空。");
        if (text.Contains('\0')) throw new InvalidDataException("文本包含无法显示的空字符，请检查文件或切换 UTF-16 编码。");
        token.ThrowIfCancellationRequested();
        return new TxtDocument(fullPath, text, codec.WebName, bytes.LongLength, modified);
    }, token);

    private static (Encoding, int) DetectEncoding(byte[] bytes, string requested)
    {
        Encoding Strict(string name) => Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        if (requested != "Auto")
        {
            var selected = Strict(requested);
            var bom = selected.GetPreamble();
            // GetEncoding("utf-8") has a BOM on some runtimes; handle it explicitly too.
            var skip = selected.CodePage == 65001 && bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 :
                bom.Length > 0 && bytes.AsSpan().StartsWith(bom) ? bom.Length : 0;
            return (selected, skip);
        }
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return (new UTF8Encoding(false, true), 3);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return (new UnicodeEncoding(false, false, true), 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return (new UnicodeEncoding(true, false, true), 2);
        // Detect BOM-less UTF-16 containing Latin text before treating NULs as UTF-8.
        var pairs = Math.Min(bytes.Length / 2, 2048);
        if (pairs > 1 && bytes.Length % 2 == 0)
        {
            var evenZeros = 0; var oddZeros = 0;
            for (var i = 0; i < pairs; i++) { if (bytes[2 * i] == 0) evenZeros++; if (bytes[2 * i + 1] == 0) oddZeros++; }
            if (oddZeros > pairs / 3 && evenZeros < pairs / 10 + 1) return (new UnicodeEncoding(false, false, true), 0);
            if (evenZeros > pairs / 3 && oddZeros < pairs / 10 + 1) return (new UnicodeEncoding(true, false, true), 0);
        }
        try { var utf8 = new UTF8Encoding(false, true); utf8.GetCharCount(bytes); return (utf8, 0); }
        catch (DecoderFallbackException) { return (Strict("gb18030"), 0); }
    }

    public int ElementIndex(int offset)
    {
        if (offset >= Text.Length) return _elements.Length;
        var index = Array.BinarySearch(_elements, Math.Max(0, offset));
        return index >= 0 ? index : Math.Max(0, ~index - 1);
    }
    public int OffsetAtElement(int index) => index >= _elements.Length ? Text.Length : _elements[Math.Max(0, index)];
    public int SnapOffset(int offset) => OffsetAtElement(ElementIndex(offset));

    public TxtPage Page(int offset, int maximum, double availableWidth, Func<string, double> measure,
        bool naturalBreaks = true, bool compressWhitespace = true)
    {
        var start = SnapOffset(offset); var index = ElementIndex(start);
        var text = new StringBuilder(); var ends = new List<int>(); var lengths = new List<int>(); var boundaries = new List<int>();
        while (index < _elements.Length && ends.Count < maximum)
        {
            var end = OffsetAtElement(index + 1);
            var element = Text[_elements[index]..end];
            var space = element.All(c => char.IsWhiteSpace(c) || char.IsControl(c));
            var display = space ? " " : element;
            var candidate = text.ToString() + display;
            if (ends.Count > 0 && measure(candidate) > availableWidth) break;
            text.Append(display); ends.Add(end); lengths.Add(text.Length);
            if (space || "。！？；.!?;，,：:".Contains(element, StringComparison.Ordinal)) boundaries.Add(ends.Count);
            if (space && compressWhitespace)
            {
                // Skip a whitespace run without allocating a string for every original character.
                while (end < Text.Length && (char.IsWhiteSpace(Text[end]) || char.IsControl(Text[end]))) end++;
                end = SnapOffset(end); // A whitespace base may carry combining marks in the next grapheme.
                ends[^1] = end; index = ElementIndex(end);
            }
            else index++;
        }
        var take = ends.Count;
        if (naturalBreaks && index < _elements.Length && boundaries.LastOrDefault(n => n >= Math.Max(1, take / 2)) is var boundary && boundary > 0)
            take = boundary;
        return take == 0 ? new(start, start, "", 0) : new(start, ends[take - 1], text.ToString(0, lengths[take - 1]), take);
    }
}
