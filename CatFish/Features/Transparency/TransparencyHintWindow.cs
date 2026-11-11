using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using CatFish.Core;
using CatFish.UI;

namespace CatFish.Features.Transparency;

public sealed class TransparencyHintWindow : Window
{
    public TransparencyHintWindow()
    {
        Title = "窗口透明 · CatFish";
        Width = 280;
        SizeToContent = SizeToContent.Height;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = ViewFactory.Shell(this);

        var shortcuts = new StackPanel();
        foreach (var (label, action) in new[]
        {
            ($"降低 {AppController.OpacityStep}%", "DecreaseOpacity"),
            ($"增加 {AppController.OpacityStep}%", "IncreaseOpacity"), ("一键透明/恢复", "ResetOpacity")
        })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var shortcut = new TextBlock
            {
                Text = AppController.Settings.Hotkeys[action], TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"], FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(shortcut, 1); row.Children.Add(shortcut); shortcuts.Children.Add(row);
        }
        root.Children.Add(shortcuts);
        root.Children.Add(ViewFactory.Muted("作用于当前焦点窗口"));
        Content = ViewFactory.Frame(this, root, "窗口透明");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
}
