using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace CatFish.Features.Popup;

public sealed record PopupBackground(string Id, string Asset)
{
    public string Name => Asset;
}

internal sealed record PopupAnimation(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<TimeSpan> Delays);

public static class PopupBackgrounds
{
    public const string DefaultId = "jd1111"; // Keep the default slot compatible with saved settings.
    private static readonly Dictionary<string, PopupAnimation> Cache = new();
    public static IReadOnlyList<PopupBackground> Items { get; } = Array.AsReadOnly<PopupBackground>([
        new(DefaultId, "找好服网.gif"),
        new("gfcb", "鬼服超变.gif"),
        new("workbuddy", "Workbuddy.png"),
        new("wxzjz", "无限转九职.gif"),
        new("xlxxj", "修罗吸血剑.png"),
        new("nano-work", "纳米Work.png")
    ]);

    public static BitmapSource Load(string id) => LoadAnimation(id).Frames[0];

    internal static PopupAnimation LoadAnimation(string id)
    {
        var background = Items.FirstOrDefault(item => item.Id == id) ?? Items[0];
        if (Cache.TryGetValue(background.Id, out var cached)) return cached;
        var uri = new Uri("pack://application:,,,/CatFish;component/Assets/PopupBackgrounds/" + background.Asset);
        using var stream = Application.GetResourceStream(uri)!.Stream;
        using var source = Drawing.Image.FromStream(stream);
        var animated = source.FrameDimensionsList.Contains(Imaging.FrameDimension.Time.Guid);
        var count = animated ? source.GetFrameCount(Imaging.FrameDimension.Time) : 1;
        var delayBytes = animated && source.PropertyIdList.Contains(0x5100) ? source.GetPropertyItem(0x5100)?.Value : null;
        var frames = new BitmapSource[count];
        var delays = new TimeSpan[count];
        // GDI+ composites GIF delta frames, transparency and disposal onto the logical canvas.
        using var canvas = new Drawing.Bitmap(source.Width, source.Height, Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Drawing.Graphics.FromImage(canvas);
        for (var index = 0; index < count; index++)
        {
            if (animated) source.SelectActiveFrame(Imaging.FrameDimension.Time, index);
            graphics.Clear(Drawing.Color.Transparent);
            graphics.DrawImage(source, new Drawing.Rectangle(0, 0, source.Width, source.Height));
            var bits = canvas.LockBits(new Drawing.Rectangle(0, 0, canvas.Width, canvas.Height),
                Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                frames[index] = BitmapSource.Create(canvas.Width, canvas.Height, 96, 96, PixelFormats.Pbgra32,
                    null, bits.Scan0, bits.Stride * canvas.Height, bits.Stride);
                frames[index].Freeze();
            }
            finally { canvas.UnlockBits(bits); }
            var delay = delayBytes is not null && delayBytes.Length >= (index + 1) * 4 ? BitConverter.ToInt32(delayBytes, index * 4) : 10;
            delays[index] = TimeSpan.FromMilliseconds(delay > 0 ? Math.Max(20L, delay * 10L) : 100);
        }
        return Cache[background.Id] = new(frames, delays);
    }
}
