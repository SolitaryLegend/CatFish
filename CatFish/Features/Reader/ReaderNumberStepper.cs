using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CatFish.Features.Reader;

internal sealed class ReaderNumberStepper : Border
{
    private static readonly ControlTemplate ArrowTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type RepeatButton}">
          <Border x:Name="Frame" Background="Transparent" CornerRadius="2">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="#E2F0E9"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Background" Value="#CAE5D8"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.3"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    private readonly Func<double> _read, _minimum, _maximum;
    private readonly Action<double> _change;
    private readonly Action<string>? _validation;
    private readonly string _caption;
    private readonly TextBox _box;
    private readonly RepeatButton _up, _down;
    private double _lastMinimum = double.NaN, _lastMaximum = double.NaN;

    public ReaderNumberStepper(string name, string caption, Func<double> read, Func<double> minimum, Func<double> maximum,
        Action<double> change, Action<string>? validation = null, bool compact = false)
    {
        _read = read; _minimum = minimum; _maximum = maximum; _change = change; _validation = validation; _caption = caption;
        Height = compact ? 24 : 28; Background = Brushes.White; BorderBrush = new SolidColorBrush(Color.FromRgb(218, 229, 224));
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(4);
        _box = new TextBox { Name = name + "Input", Text = Format(read()), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Padding = compact ? new Thickness(3, 1, 15, 1) : new Thickness(4, 1, 20, 1), FontSize = 11 };
        System.Windows.Automation.AutomationProperties.SetName(_box, caption);
        _up = Arrow(name + "Increase", "▴", "增大" + caption, 1);
        _down = Arrow(name + "Decrease", "▾", "减小" + caption, -1);
        var arrows = new Grid { Width = compact ? 13 : 17, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 1, 1, 1) };
        arrows.RowDefinitions.Add(new()); arrows.RowDefinitions.Add(new()); arrows.Children.Add(_up); Grid.SetRow(_down, 1); arrows.Children.Add(_down);
        var editor = new Grid(); editor.Children.Add(_box); editor.Children.Add(arrows); Child = editor;
        _box.TextChanged += (_, _) => RefreshArrows();
        _box.LostKeyboardFocus += (_, _) => Commit();
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Commit(); else if (e.Key == Key.Up) Step(1); else if (e.Key == Key.Down) Step(-1); else return;
            e.Handled = true;
        };
        ReaderInputWheel.Attach(this, Step);
        Refresh();
    }

    public void Refresh()
    {
        if (!_box.IsKeyboardFocusWithin || _lastMinimum != _minimum() || _lastMaximum != _maximum()) _box.Text = Format(_read());
        _lastMinimum = _minimum(); _lastMaximum = _maximum();
        _box.ToolTip = $"{_caption}范围 {_minimum()}～{_maximum()}；支持输入、箭头及悬停滚轮调整";
        RefreshArrows();
    }
    private RepeatButton Arrow(string name, string text, string hint, int direction)
    {
        var button = new RepeatButton { Name = name, Content = text, ToolTip = hint, Template = ArrowTemplate,
            Focusable = false, IsTabStop = false, FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(104, 126, 116)), Delay = 350, Interval = 70 };
        System.Windows.Automation.AutomationProperties.SetName(button, hint);
        button.Click += (_, _) => Step(direction); return button;
    }
    private bool TryValue(out double value) => double.TryParse(_box.Text, out value) && double.IsFinite(value) && value >= _minimum() && value <= _maximum();
    private double PendingValue() => TryValue(out var value) ? value : _read();
    private void RefreshArrows() { _up.IsEnabled = PendingValue() < _maximum(); _down.IsEnabled = PendingValue() > _minimum(); }
    private void Step(int direction)
    {
        var value = Math.Clamp(PendingValue() + direction, _minimum(), _maximum());
        _box.Text = Format(value); if (Math.Abs(value - _read()) > .001) _change(value); RefreshArrows();
    }
    public void Commit()
    {
        if (TryValue(out var value)) { if (Math.Abs(value - _read()) > .001) _change(value); }
        else { _box.Text = Format(_read()); _validation?.Invoke($"{_caption}范围为 {_minimum()}～{_maximum()}。"); }
        RefreshArrows();
    }
    private static string Format(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);
}
