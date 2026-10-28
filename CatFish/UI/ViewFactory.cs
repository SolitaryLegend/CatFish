using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using CatFish.Core;

namespace CatFish.UI;

internal static class ViewFactory
{
    public static StackPanel Shell(Window window)
    {
        window.Background = (Brush)Application.Current.Resources["CanvasBrush"];
        window.Foreground = (Brush)Application.Current.Resources["TextBrush"];
        window.UseLayoutRounding = true;
        window.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        window.FontSize = 12;
        return new StackPanel { Margin = new Thickness(12) };
    }

    public static Border Frame(Window window, UIElement content, string title, bool compact = false)
    {
        EnableEscapeClose(window);
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        var layout = new DockPanel();
        var header = new DockPanel { Margin = compact ? new Thickness(4, 2, 4, 0) : new Thickness(12, 8, 12, 0), Background = Brushes.Transparent };
        var close = new Button
        {
            Name = "WindowCloseButton", Content = "×", Width = compact ? 18 : 22, Height = compact ? 18 : 22, Padding = new Thickness(0),
            Background = Brushes.Transparent, Foreground = MutedBrush, Focusable = false, IsTabStop = false, ToolTip = "关闭（Esc）"
        };
        System.Windows.Automation.AutomationProperties.SetName(close, "关闭");
        close.Click += (_, _) => window.Close();
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock
        {
            Text = title, Foreground = (Brush)Application.Current.Resources["TextBrush"],
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) window.DragMove(); };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header); layout.Children.Add(content);
        return new Border
        {
            Background = (Brush)Application.Current.Resources["CanvasBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrushSoft"],
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(compact ? 4 : 6), Child = layout
        };
    }

    public static void EnableEscapeClose(Window window)
    {
        window.KeyDown -= CloseOnEscape;
        window.KeyDown += CloseOnEscape;
    }

    private static void CloseOnEscape(object sender, KeyEventArgs e)
    {
        // Let controls handle Escape first (e.g. close a dropdown or cancel hotkey capture).
        // Reuse normal closing so settings cleanup and the launcher's tray behavior stay intact.
        if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None || e.IsRepeat) return;
        e.Handled = true;
        ((Window)sender).Close();
    }

    public static Border Card(UIElement content, Thickness? margin = null) => new()
    {
        Background = (Brush)Application.Current.Resources["SurfaceBrush"], BorderBrush = (Brush)Application.Current.Resources["BorderBrushSoft"],
        BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(5), Padding = new Thickness(10), Margin = margin ?? new Thickness(0, 0, 0, 8), Child = content
    };

    public static Button Action(string text, RoutedEventHandler click, bool secondary = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 4) };
        if (secondary) { button.Background = new SolidColorBrush(Color.FromRgb(232, 241, 236)); button.Foreground = (Brush)Application.Current.Resources["TextBrush"]; }
        button.Click += click; return button;
    }

    public static TextBlock Muted(string text) => new() { Text = text, Foreground = (Brush)Application.Current.Resources["MutedBrush"], FontSize = 12, TextWrapping = TextWrapping.Wrap };
    public static TabControl Tabs(params (string Title, UIElement Content)[] pages)
    {
        var tabs = new TabControl();
        foreach (var page in pages) tabs.Items.Add(new TabItem
        {
            Header = page.Title,
            Content = new ScrollViewer { Foreground = (Brush)Application.Current.Resources["TextBrush"], VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = page.Content }
        });
        return tabs;
    }

    public static Brush MutedBrush => (Brush)Application.Current.Resources["MutedBrush"];
    public static TextBlock Status(string text = "")
    {
        var status = new TextBlock { Text = text, Height = 32, Foreground = MutedBrush, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
        status.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = status });
        return status;
    }

    public static StackPanel FieldRow(string label, params UIElement[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 54, VerticalAlignment = VerticalAlignment.Center });
        foreach (var control in controls) row.Children.Add(control);
        return row;
    }
    public static nint MainHandle => AppController.MainWindow is null ? nint.Zero : new System.Windows.Interop.WindowInteropHelper(AppController.MainWindow).Handle;
}
