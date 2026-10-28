namespace CatFish.Features.Popup;

internal static class PopupLayout
{
    public const int Version = 6;
    public const int TitleBarHeight = 30;
    public const int MinWidth = 288;
    public const int MinHeight = 192;

    public static (int Width, int Height) SquareSize(int width)
    {
        var side = Math.Clamp(width, MinWidth, 700 - TitleBarHeight);
        return (side, side + TitleBarHeight);
    }
}
