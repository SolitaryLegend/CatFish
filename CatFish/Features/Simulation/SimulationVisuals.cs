using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace CatFish.Features.Simulation;

internal static class SimulationVisuals
{
    internal static Viewbox Boot(bool windows11)
    {
        var canvas = new Canvas { Width = 960, Height = 540, Background = Brushes.Black };
        var logo = new ShapePath
        {
            Width = 106, Height = 106, Stretch = Stretch.Uniform,
            Fill = new SolidColorBrush(windows11 ? Color.FromRgb(0, 120, 212) : Color.FromRgb(0, 164, 239)),
            Data = Geometry.Parse(windows11
                ? "M0,0 H51 V51 H0 Z M55,0 H106 V51 H55 Z M0,55 H51 V106 H0 Z M55,55 H106 V106 H55 Z"
                : "M0,15 L42,9 V50 H0 Z M46,8 L106,0 V50 H46 Z M0,55 H42 V96 L0,90 Z M46,55 H106 V106 L46,97 Z")
        };
        Canvas.SetLeft(logo, 427);
        Canvas.SetTop(logo, 157);
        canvas.Children.Add(logo);

        var spinner = BootSpinner(windows11);
        Canvas.SetLeft(spinner, 464);
        Canvas.SetTop(spinner, 351);
        canvas.Children.Add(spinner);

        var preview = new Viewbox { Child = canvas };
        AutomationProperties.SetName(preview, windows11 ? "Windows 11 开机动画" : "Windows 10 开机动画");
        return preview;
    }

    private static Canvas BootSpinner(bool windows11)
    {
        var spinner = new Canvas { Width = 32, Height = 32 };
        if (windows11)
        {
            spinner.Children.Add(new ShapePath
            {
                Data = Geometry.Parse("M16,3 A13,13 0 1 1 4.7,22.5"), Stroke = Brushes.White, StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
            AnimateRotation(spinner, TimeSpan.FromSeconds(1.35), 0, TimeSpan.Zero);
        }
        else
        {
            for (var i = 0; i < 5; i++)
            {
                var orbit = new Canvas { Width = 32, Height = 32 };
                var dot = new Ellipse { Width = 3, Height = 3, Fill = Brushes.White, Opacity = 1 - i * .1 };
                Canvas.SetLeft(dot, 14.5);
                Canvas.SetTop(dot, 2.5);
                orbit.Children.Add(dot);
                // Each dot trails the preceding one and closes the gap as it slows down.
                AnimateRotation(orbit, TimeSpan.FromSeconds(1.65), -8 * i, TimeSpan.FromMilliseconds(55 * i));
                spinner.Children.Add(orbit);
            }
        }
        return spinner;
    }

    private static void AnimateRotation(Canvas orbit, TimeSpan duration, double startAngle, TimeSpan delay)
    {
        var rotation = new RotateTransform(startAngle);
        orbit.RenderTransform = rotation;
        orbit.RenderTransformOrigin = new Point(.5, .5);
        orbit.Loaded += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(startAngle, startAngle + 360, duration)
            {
                BeginTime = delay,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new BootRotationEase { EasingMode = EasingMode.EaseIn }
            });
        orbit.Unloaded += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private sealed class BootRotationEase : EasingFunctionBase
    {
        // Slow at the start and end of each turn, with the same nonzero speed at the loop seam.
        protected override double EaseInCore(double time) => time - .86 * Math.Sin(Math.Tau * time) / Math.Tau;

        protected override Freezable CreateInstanceCore() => new BootRotationEase();
    }

    internal static Viewbox UuPrivacy()
    {
        const double messageFontSize = 15;
        var canvas = new Grid { Width = 960, Height = 540, Background = Brushes.Black };
        var message = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        message.Children.Add(UuBrand());
        message.Children.Add(new TextBlock
        {
            Text = "本机已开启黑屏防护，当前正在被远程控制。", Foreground = Brushes.Gainsboro,
            FontSize = messageFontSize, Margin = new Thickness(0, 8, 0, 0), TextAlignment = TextAlignment.Center,
            RenderTransform = new TranslateTransform(messageFontSize / 2, 0)
        });
        canvas.Children.Add(message);
        return new Viewbox { Child = canvas };
    }

    private static Viewbox UuBrand()
    {
        var source = BitmapFrame.Create(new Uri("pack://application:,,,/CatFish;component/Assets/Simulation/UuRemote.png"));
        const int wordmarkStart = 70;
        const int top = 4;
        const int height = 60;
        var wordmarkWidth = source.PixelWidth - wordmarkStart;
        var mark = new CroppedBitmap(source, new Int32Rect(0, top, wordmarkStart, height));
        var wordmark = new CroppedBitmap(source, new Int32Rect(wordmarkStart, top, wordmarkWidth, height));
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(new Image { Source = mark, Width = wordmarkStart, Height = height });
        // Keep the supplied lettering, using its alpha as a mask so it reads on black.
        brand.Children.Add(new Rectangle
        {
            Width = wordmarkWidth, Height = height, Fill = Brushes.White,
            OpacityMask = new ImageBrush(wordmark) { Stretch = Stretch.Fill }
        });

        var view = new Viewbox { Width = 212, HorizontalAlignment = HorizontalAlignment.Center, Child = brand };
        AutomationProperties.SetName(view, "网易UU远程");
        return view;
    }
}
