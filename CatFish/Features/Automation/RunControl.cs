using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;

namespace CatFish.Features.Automation;

public sealed class RunControl : UserControl
{
    private readonly Button _button = new();
    private bool _isRunning;
    public event Action<bool>? Requested;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            _isRunning = value;
            _button.Content = value ? "停止" : "开始";
            _button.Background = value ? (Brush)Application.Current.Resources["AccentBrush"] : new SolidColorBrush(Color.FromRgb(232, 241, 236));
            _button.Foreground = value ? Brushes.White : (Brush)Application.Current.Resources["TextBrush"];
        }
    }
    public RunControl()
    {
        HorizontalAlignment = HorizontalAlignment.Left; Width = 110; Height = 32; Margin = new Thickness(0, 6, 0, 0);
        _button.SetResourceReference(StyleProperty, "RunSegmentStyle");
        _button.Click += (_, _) => Requested?.Invoke(!IsRunning);
        Content = _button; IsRunning = false;
    }
}
