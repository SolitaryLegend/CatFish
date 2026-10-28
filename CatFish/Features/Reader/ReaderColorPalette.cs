using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CatFish.Features.Reader;

/// <summary>A compact color-only picker; selection previews live and commits on release.</summary>
internal sealed class ReaderColorPalette : Border
{
    private static readonly ControlTemplate HueTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type Slider}">
          <Grid Margin="4,0">
            <Border Height="10" CornerRadius="4" VerticalAlignment="Center">
              <Border.Background><LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                <GradientStop Color="#FF0000" Offset="0"/><GradientStop Color="#FFFF00" Offset="0.166667"/>
                <GradientStop Color="#00FF00" Offset="0.333333"/><GradientStop Color="#00FFFF" Offset="0.5"/>
                <GradientStop Color="#0000FF" Offset="0.666667"/><GradientStop Color="#FF00FF" Offset="0.833333"/>
                <GradientStop Color="#FF0000" Offset="1"/>
              </LinearGradientBrush></Border.Background>
            </Border>
            <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}"
                   Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
              <Track.Thumb><Thumb Width="8" Height="18"><Thumb.Template><ControlTemplate TargetType="Thumb">
                <Border Background="#DFFFFFFF" BorderBrush="#688479" BorderThickness="1" CornerRadius="3"/>
              </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
        """);
    private readonly Grid _field = new() { Width = 224, Height = 130, ClipToBounds = true, Cursor = Cursors.Cross, Focusable = true };
    private readonly Canvas _markerLayer = new() { IsHitTestVisible = false };
    private readonly Slider _hue = new() { Minimum = 0, Maximum = 360, Height = 20, Margin = new Thickness(0, 8, 0, 7), SmallChange = 1, LargeChange = 15, IsMoveToPointEnabled = true, Template = HueTemplate };
    private readonly Ellipse _marker = new() { Width = 10, Height = 10, Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent };
    private double _angle, _saturation, _value;
    private bool _updating;
    public event Action<Color, bool>? ColorSelected;

    public ReaderColorPalette()
    {
        Background = Brushes.White; BorderBrush = new SolidColorBrush(Color.FromRgb(209, 224, 216));
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(7); Padding = new Thickness(10);
        var root = new StackPanel(); Child = root;
        _field.Children.Add(new Border { Background = new LinearGradientBrush(Colors.White, Colors.Transparent, new Point(0, 0), new Point(1, 0)), IsHitTestVisible = false });
        _field.Children.Add(new Border { Background = new LinearGradientBrush(Colors.Transparent, Colors.Black, new Point(0, 0), new Point(0, 1)), IsHitTestVisible = false });
        _markerLayer.Children.Add(new Ellipse { Width = 12, Height = 12, Stroke = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), StrokeThickness = 1 });
        _markerLayer.Children.Add(_marker); _field.Children.Add(_markerLayer); root.Children.Add(_field); root.Children.Add(_hue);
        _field.MouseLeftButtonDown += (_, e) => { _field.CaptureMouse(); SelectPoint(e.GetPosition(_field), false); e.Handled = true; };
        _field.MouseMove += (_, e) => { if (_field.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) SelectPoint(e.GetPosition(_field), false); };
        _field.MouseLeftButtonUp += (_, e) =>
        { if (_field.IsMouseCaptured) { SelectPoint(e.GetPosition(_field), true); _field.ReleaseMouseCapture(); e.Handled = true; } };
        _field.SizeChanged += (_, _) => UpdateVisuals();
        _hue.ValueChanged += (_, _) => { if (_updating) return; _angle = _hue.Value; UpdateVisuals(); ColorSelected?.Invoke(ToColor(_angle, _saturation, _value), false); };
        var colors = new UniformGrid { Columns = 8 };
        foreach (var hex in new[] { "#FFFFFF", "#293C35", "#6B7280", "#30977A", "#E05252", "#E9A03F", "#4E84C4", "#926FC1" })
        {
            var selected = (Color)ColorConverter.ConvertFromString(hex);
            var button = new Button { Name = "ReaderQuickColor" + hex[1..], Background = new SolidColorBrush(selected), BorderBrush = new SolidColorBrush(Color.FromRgb(213, 224, 219)),
                BorderThickness = new Thickness(1), Height = 22, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(0), ToolTip = hex, Focusable = false };
            System.Windows.Automation.AutomationProperties.SetName(button, "选择颜色 " + hex);
            button.Click += (_, _) => { SetColor(selected); ColorSelected?.Invoke(selected, true); }; colors.Children.Add(button);
        }
        root.Children.Add(colors);
        SetColor(Color.FromRgb(41, 60, 53));
    }

    public void SetColor(Color color)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var maximum = Math.Max(r, Math.Max(g, b)); var delta = maximum - Math.Min(r, Math.Min(g, b));
        _angle = delta < .00001 ? 0 : maximum == r ? 60 * (((g - b) / delta + 6) % 6) : maximum == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        _saturation = maximum == 0 ? 0 : delta / maximum; _value = maximum;
        _updating = true; _hue.Value = _angle; _updating = false; UpdateVisuals();
    }
    private void SelectPoint(Point point, bool committed)
    {
        _saturation = Math.Clamp(point.X / Math.Max(1, _field.ActualWidth), 0, 1);
        _value = 1 - Math.Clamp(point.Y / Math.Max(1, _field.ActualHeight), 0, 1);
        UpdateVisuals(); ColorSelected?.Invoke(ToColor(_angle, _saturation, _value), committed);
    }
    private void UpdateVisuals()
    {
        _field.Background = new SolidColorBrush(ToColor(_angle, 1, 1));
        var x = Math.Clamp(_saturation * _field.Width - 5, 1, _field.Width - 11);
        var y = Math.Clamp((1 - _value) * _field.Height - 5, 1, _field.Height - 11);
        Canvas.SetLeft(_marker, x); Canvas.SetTop(_marker, y);
        Canvas.SetLeft(_markerLayer.Children[0], x - 1); Canvas.SetTop(_markerLayer.Children[0], y - 1);
    }
    private static Color ToColor(double hue, double saturation, double value)
    {
        var chroma = value * saturation; var component = chroma * (1 - Math.Abs(hue / 60 % 2 - 1)); var minimum = value - chroma;
        var (r, g, b) = (hue % 360) switch
        {
            < 60 => (chroma, component, 0d), < 120 => (component, chroma, 0d), < 180 => (0d, chroma, component),
            < 240 => (0d, component, chroma), < 300 => (component, 0d, chroma), _ => (chroma, 0d, component)
        };
        return Color.FromRgb((byte)Math.Round((r + minimum) * 255), (byte)Math.Round((g + minimum) * 255), (byte)Math.Round((b + minimum) * 255));
    }
}
