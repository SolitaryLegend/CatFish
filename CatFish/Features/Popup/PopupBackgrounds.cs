using System.Windows.Media.Imaging;

namespace CatFish.Features.Popup;

public sealed record PopupBackground(string Id, string Name, string Asset);

public static class PopupBackgrounds
{
    private static readonly Dictionary<string, BitmapSource> Cache = new();
    public static IReadOnlyList<PopupBackground> Items { get; } = Array.AsReadOnly<PopupBackground>([
        new("shopping", "购物狂欢 · 红金", "AdBackgroundShoppingSquare.png"),
        new("gaming", "游戏礼包 · 霓虹", "AdBackgroundGamingSquare.png"),
        new("travel", "度假好礼 · 海岛", "AdBackgroundTravelSquare.png"),
        new("coffee", "咖啡会员 · 焦糖", "AdBackgroundCoffeeSquare.png"),
        new("legacy", "淘宝双11 · 红金盛典", "AdBackgroundTaobaoSquare.png")
    ]);

    public static BitmapSource Load(string id)
    {
        var background = Items.FirstOrDefault(item => item.Id == id) ?? Items[0];
        if (Cache.TryGetValue(background.Id, out var cached)) return cached;
        BitmapSource result = new BitmapImage(new Uri("pack://application:,,,/CatFish;component/Assets/" + background.Asset));
        result.Freeze(); Cache[background.Id] = result; return result;
    }
}
