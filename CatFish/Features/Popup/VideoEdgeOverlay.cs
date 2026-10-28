using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows;
using CatFish.Core;

namespace CatFish.Features.Popup;

internal sealed class VideoEdgeOverlay : Window
{
    private readonly Image _topImage = new() { Stretch = Stretch.UniformToFill };
    private readonly Image _bottomImage = new() { Stretch = Stretch.UniformToFill };
    private readonly Image _leftImage = new() { Stretch = Stretch.UniformToFill };
    private readonly Image _rightImage = new() { Stretch = Stretch.UniformToFill };
    private readonly Image _videoImage = new() { Stretch = Stretch.UniformToFill };
    private readonly Grid _top = new() { VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
    private readonly Grid _bottom = new() { VerticalAlignment = VerticalAlignment.Bottom, ClipToBounds = true };
    private readonly Grid _left = new() { HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
    private readonly Grid _right = new() { HorizontalAlignment = HorizontalAlignment.Right, ClipToBounds = true };
    private readonly Grid _videoBlend = new() { ClipToBounds = true, Background = Brushes.Black };

    public VideoEdgeOverlay(Window owner)
    {
        Owner = owner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowActivated = false;
        ShowInTaskbar = false; Topmost = true; IsHitTestVisible = false;
        var root = new Grid { IsHitTestVisible = false };
        foreach (var (surface, image) in new[] { (_videoBlend, _videoImage), (_left, _leftImage), (_right, _rightImage), (_top, _topImage), (_bottom, _bottomImage) })
        {
            var canvas = new Canvas { ClipToBounds = true }; canvas.Children.Add(image);
            surface.Children.Add(canvas); root.Children.Add(surface);
        }
        Content = root;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new nint(style | 0x20 | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
            HwndSource.FromHwnd(handle)?.AddHook(HitTest);
        };
    }

    private static nint HitTest(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x84) { handled = true; return new nint(-1); }
        return nint.Zero;
    }

    public void UpdateBounds(FrameworkElement host, Image background, AppSettings settings)
    {
        var videoOpacity = Math.Clamp(settings.PopupVideoOpacity, 0, 1);
        if (!host.IsVisible || host.ActualWidth < 1 || host.ActualHeight < 1
            || (settings.PopupGradientHeight <= 0 && settings.PopupSideMaskWidth <= 0 && videoOpacity >= 1))
        { Hide(); return; }
        var origin = host.PointToScreen(new Point(0, 0));
        var end = host.PointToScreen(new Point(host.ActualWidth, host.ActualHeight));
        var offset = host.TranslatePoint(new Point(0, 0), background);
        Width = host.ActualWidth; Height = host.ActualHeight;
        var coveredHeight = Math.Clamp(settings.PopupGradientHeight, 0, host.ActualHeight / 2);
        var edgeHeight = coveredHeight > 0 ? Math.Min(coveredHeight + 16, host.ActualHeight / 2) : 0;
        _top.Height = edgeHeight; _bottom.Height = edgeHeight;
        _top.Visibility = _bottom.Visibility = edgeHeight > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (edgeHeight > 0)
        {
            _top.OpacityMask = Fade(true, false, coveredHeight / edgeHeight);
            _bottom.OpacityMask = Fade(false, false, coveredHeight / edgeHeight);
            ApplyBackground(_topImage, background, offset.X, offset.Y, settings.PopupOpacity, settings.PopupBlurRadius);
            ApplyBackground(_bottomImage, background, offset.X, offset.Y + host.ActualHeight - edgeHeight, settings.PopupOpacity, settings.PopupBlurRadius);
        }
        var coveredWidth = Math.Clamp(settings.PopupSideMaskWidth, 0, host.ActualWidth / 2);
        var edgeWidth = coveredWidth > 0 ? Math.Min(coveredWidth + 16, host.ActualWidth / 2) : 0;
        _left.Width = edgeWidth; _right.Width = edgeWidth;
        _left.Visibility = _right.Visibility = edgeWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (edgeWidth > 0)
        {
            _left.OpacityMask = Fade(true, true, coveredWidth / edgeWidth);
            _right.OpacityMask = Fade(false, true, coveredWidth / edgeWidth);
            ApplyBackground(_leftImage, background, offset.X, offset.Y, settings.PopupOpacity, settings.PopupBlurRadius);
            ApplyBackground(_rightImage, background, offset.X + host.ActualWidth - edgeWidth, offset.Y, settings.PopupOpacity, settings.PopupBlurRadius);
        }
        _videoBlend.Opacity = 1 - videoOpacity;
        _videoBlend.Visibility = videoOpacity < 1 ? Visibility.Visible : Visibility.Collapsed;
        ApplyBackground(_videoImage, background, offset.X, offset.Y, settings.PopupOpacity, 0);
        if (!IsVisible) Show();
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, NativeMethods.HwndTopmost,
            (int)Math.Round(origin.X), (int)Math.Round(origin.Y), Math.Max(1, (int)Math.Round(end.X - origin.X)),
            Math.Max(1, (int)Math.Round(end.Y - origin.Y)), NativeMethods.SwpNoActivate);
        UpdateLayout();
        ((UIElement)Content).InvalidateVisual();
    }

    private static void ApplyBackground(Image image, Image background, double offsetX, double offsetY, double opacity, int blurRadius)
    {
        image.Source = background.Source; image.Width = background.ActualWidth; image.Height = background.ActualHeight;
        image.Stretch = background.Stretch;
        image.Opacity = opacity; Canvas.SetLeft(image, -offsetX); Canvas.SetTop(image, -offsetY);
        if (blurRadius > 0)
        {
            if (image.Effect is not BlurEffect blur) image.Effect = new BlurEffect { Radius = blurRadius };
            else blur.Radius = blurRadius;
        }
        else image.Effect = null;
    }

    private static LinearGradientBrush Fade(bool leading, bool horizontal, double coveredFraction)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = horizontal ? new Point(1, 0) : new Point(0, 1) };
        brush.GradientStops.Add(new(leading ? Colors.White : Colors.Transparent, 0));
        brush.GradientStops.Add(new(Colors.White, leading ? coveredFraction : 1 - coveredFraction));
        brush.GradientStops.Add(new(leading ? Colors.Transparent : Colors.White, 1));
        return brush;
    }
}
