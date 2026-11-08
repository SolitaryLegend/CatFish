namespace CatFish.Features.Popup;

internal static class PopupLayout
{
    public const int Version = 7;
    public const int TitleBarHeight = 30;
    public const int MinWidth = 288;
    public const int MinHeight = 192;

    public static (int Width, int Height) SquareSize(int width)
    {
        var side = Math.Clamp(width, MinWidth, 700 - TitleBarHeight);
        return (side, side + TitleBarHeight);
    }

    public static (int Width, int Height) BackgroundSize(int width, string backgroundId)
    {
        var image = PopupBackgrounds.Load(backgroundId);
        var ratio = image.PixelWidth / (double)image.PixelHeight;
        var minWidth = Math.Max(MinWidth, (MinHeight - TitleBarHeight) * ratio);
        var maxWidth = Math.Min(900, (700 - TitleBarHeight) * ratio);
        var fittedWidth = (int)Math.Round(Math.Clamp(width, Math.Min(minWidth, maxWidth), maxWidth));
        return (fittedWidth, Math.Clamp((int)Math.Round(fittedWidth / ratio, MidpointRounding.AwayFromZero) + TitleBarHeight, MinHeight, 700));
    }
}
