using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using CatFish.Core;

namespace CatFish.UI;

public sealed class WindowPickerWindow : Window
{
    private readonly bool _multiple;
    private readonly bool _includeCatFish;
    private readonly List<Choice> _choices = new();
    private readonly ListBox _list = new();
    private readonly TextBox _search = new() { ToolTip = "搜索窗口标题" };
    private readonly TextBlock _refreshStatus = new() { Height = 20, FontSize = 11, Foreground = ViewFactory.MutedBrush };
    private readonly TextBlock _count = new() { Foreground = ViewFactory.MutedBrush, VerticalAlignment = VerticalAlignment.Center };
    public IReadOnlyList<WindowTarget> SelectedTargets => _choices.Where(c => c.Selected).Select(c => c.Target).ToList();

    public WindowPickerWindow(Window? owner, bool multiple, IEnumerable<nint> selected, string? pickerTitle = null, bool includeCatFish = true)
    {
        Owner = owner; _multiple = multiple; _includeCatFish = includeCatFish; Title = pickerTitle ?? (multiple ? "选择切换应用" : "选择滚动应用");
        Width = 310; Height = 390; MinWidth = 290; MinHeight = 300; WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ViewFactory.Shell(this);
        var root = new DockPanel { Margin = new Thickness(12) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var refresh = ViewFactory.Action("刷新", (_, _) => { _search.Clear(); Refresh(); }, true); DockPanel.SetDock(refresh, Dock.Right); top.Children.Add(refresh); top.Children.Add(_search);
        var header = new StackPanel(); header.Children.Add(top); header.Children.Add(_refreshStatus); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var confirm = ViewFactory.Action("确定", (_, _) => { DialogResult = true; }); confirm.IsDefault = true;
        var cancel = ViewFactory.Action("取消", (_, _) => { DialogResult = false; }, true); cancel.IsCancel = true;
        DockPanel.SetDock(confirm, Dock.Right); footer.Children.Add(confirm); DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel); footer.Children.Add(_count);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var row = new FrameworkElementFactory(typeof(CheckBox)); row.SetBinding(ContentControl.ContentProperty, new Binding(nameof(Choice.Name))); row.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(Choice.Selected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        row.SetValue(MarginProperty, new Thickness(0)); row.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        row.SetValue(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch); row.SetBinding(ToolTipProperty, new Binding(nameof(Choice.Name)));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding()); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        row.SetValue(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = title });
        _list.ItemTemplate = new DataTemplate { VisualTree = row }; _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        root.Children.Add(_list); Content = ViewFactory.Frame(this, root, Title);
        _search.TextChanged += (_, _) => Filter();
        Refresh(selected.ToHashSet());
    }

    private void Refresh(HashSet<nint>? selection = null)
    {
        selection ??= _choices.Where(c => c.Selected).Select(c => c.Target.Handle).ToHashSet();
        _choices.Clear();
        foreach (var target in new WindowDiscoveryService().GetEligibleWindows(ViewFactory.MainHandle, !_includeCatFish))
        {
            if (!_includeCatFish && target.IsCatFish) continue;
            var choice = new Choice(target, selection.Contains(target.Handle)); choice.PropertyChanged += SelectionChanged; _choices.Add(choice);
        }
        Filter();
        _refreshStatus.Text = $"已刷新 {_choices.Count} 个窗口 · {DateTime.Now:HH:mm:ss}";
    }
    private void Filter()
    {
        var query = _search.Text.Trim(); _list.ItemsSource = _choices.Where(c => query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        _count.Text = $"已选 {_choices.Count(c => c.Selected)} 个";
    }
    private void SelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_multiple && sender is Choice { Selected: true } chosen)
            foreach (var other in _choices.Where(c => !ReferenceEquals(c, chosen) && c.Selected)) other.Selected = false;
        _count.Text = $"已选 {_choices.Count(c => c.Selected)} 个";
    }

    private sealed class Choice(WindowTarget target, bool selected) : INotifyPropertyChanged
    {
        public WindowTarget Target { get; } = target;
        public string Name => Target.DisplayName;
        private bool _selected = selected;
        public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
