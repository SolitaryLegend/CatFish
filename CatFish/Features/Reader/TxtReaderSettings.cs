namespace CatFish.Features.Reader;

public sealed class TxtReaderSettings
{
    public int MaxCharacters { get; set; } = 32;
    public bool PreferNaturalBreaks { get; set; } = true;
    public bool CompressWhitespace { get; set; } = true;
    public string Mode { get; set; } = "Static";
    public double PixelsPerSecond { get; set; } = 40;
    public double SecondsPerPage { get; set; } = 6;
    public bool PauseOnManualNavigation { get; set; }
    public string EndBehavior { get; set; } = "Stop";
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 22;
    public bool Bold { get; set; }
    public string TextColor { get; set; } = "#293C35";
    public double TextOpacity { get; set; } = 1;
    public double BackgroundOpacity { get; set; }
    public double LineHeight { get; set; } = 1.6;
    public string Alignment { get; set; } = "Left";
    public bool Topmost { get; set; } = true;
    public bool Locked { get; set; }
    public bool MouseWheelPaging { get; set; }
    public bool ShowOnStartup { get; set; }
    public double Width { get; set; } = 760;
    public double Height { get; set; } = 44;
    public string? MonitorName { get; set; }
    public double? RelativeLeft { get; set; }
    public double? RelativeTop { get; set; }
    public string? LastFile { get; set; }
    public List<TxtReadingRecord> RecentFiles { get; set; } = new();

    public void Normalize()
    {
        MaxCharacters = Math.Clamp(MaxCharacters, 8, 64);
        FontSize = Finite(FontSize, 22, 10, 72);
        Width = Finite(Width, 760, 160, 2400); Height = Finite(Height, 44, 24, 180);
        PixelsPerSecond = Finite(PixelsPerSecond, 40, 10, 200);
        SecondsPerPage = Finite(SecondsPerPage, 6, 1, 30);
        TextOpacity = Finite(TextOpacity, 1, 0, 1); BackgroundOpacity = Finite(BackgroundOpacity, 0, 0, 1);
        LineHeight = Finite(LineHeight, 1.6, 1.2, 2.5);
        if (RelativeLeft is double x && !double.IsFinite(x)) RelativeLeft = null;
        if (RelativeTop is double y && !double.IsFinite(y)) RelativeTop = null;
        if (Mode is not ("Static" or "Scroll")) Mode = "Static";
        if (EndBehavior is not ("Stop" or "Loop" or "Hide")) EndBehavior = "Stop";
        if (Alignment is not ("Left" or "Center" or "Right")) Alignment = "Left";
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = "Microsoft YaHei UI";
        RecentFiles = (RecentFiles ?? new()).Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Path))
            .DistinctBy(r => r.Path, StringComparer.OrdinalIgnoreCase).OrderByDescending(r => r.LastOpenedUtc).Take(10).ToList();
        foreach (var record in RecentFiles) record.Offset = Math.Max(0, record.Offset);
    }

    private static double Finite(double value, double fallback, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

public sealed class TxtReadingRecord
{
    public string Path { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public DateTime LastOpenedUtc { get; set; }
    // Original UTF-16 offset, never a page number or an offset into normalized display text.
    public int Offset { get; set; }
    public string Encoding { get; set; } = "Auto";
    public override string ToString() => System.IO.Path.GetFileName(Path) +
        (System.IO.File.Exists(Path) ? "" : "  · 文件已移动或删除");
}
