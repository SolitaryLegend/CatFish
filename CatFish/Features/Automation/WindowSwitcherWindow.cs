using System.Windows.Controls;
using System.Windows;
using CatFish.Core;
using CatFish.UI;

namespace CatFish.Features.Automation;

public sealed class WindowSwitcherWindow : Window
{
    private readonly WindowAutomationService _automation;
    private readonly ComboBox _mode = new() { Width = 100 };
    private readonly TextBox _minSwitch = new() { Text = AppController.Settings.SwitchMinSeconds.ToString(), Width = 44 };
    private readonly TextBox _maxSwitch = new() { Text = AppController.Settings.SwitchMaxSeconds.ToString(), Width = 44 };
    private readonly Button _scrollPicker = new() { Content = "选择应用", Width = 210, HorizontalContentAlignment = HorizontalAlignment.Left };
    private readonly TextBox _minScroll = new() { Text = AppController.Settings.ScrollMinSeconds.ToString(), Width = 44 };
    private readonly TextBox _maxScroll = new() { Text = AppController.Settings.ScrollMaxSeconds.ToString(), Width = 44 };
    private readonly ComboBox _direction = new() { Width = 100 };
    private readonly Slider _rows = new() { Minimum = 1, Maximum = 10, Value = AppController.Settings.ScrollRows, Width = 140, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly CheckBox _randomRows = new() { Content = "随机滚动行数（1～上限）", IsChecked = AppController.Settings.ScrollRandomRows };
    private readonly CheckBox _noRepeat = new() { Content = "优先选择不同窗口", IsChecked = AppController.Settings.PreferDifferentWindow };
    private readonly CheckBox _autoActivate = new() { Content = "滚动前允许激活目标窗口", IsChecked = AppController.Settings.ScrollMayActivateTarget };
    private readonly CheckBox _randomPause = new() { Content = "滚动间随机停顿", IsChecked = AppController.Settings.ScrollRandomPause };
    private readonly TextBlock _selectedCount = new() { Foreground = ViewFactory.MutedBrush, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _rowsValue = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly RunControl _switchRun = new();
    private readonly RunControl _scrollRun = new();
    private readonly TextBlock _switchStatus = ViewFactory.Status();
    private readonly TextBlock _scrollStatus = ViewFactory.Status();
    private bool _ready;

    public WindowSwitcherWindow(WindowAutomationService? automation = null)
    {
        _automation = automation ?? AppController.Automation;
        Title = "切换与滚动 · CatFish"; Width = 370; Height = 430; MinWidth = 350; MinHeight = 350; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        foreach (var mode in new[] { "随机切换", "顺序切换", "手动模式" }) _mode.Items.Add(mode);
        _mode.SelectedIndex = AppController.Settings.SwitchMode switch { "Ordered" => 1, "Manual" => 2, _ => 0 };
        foreach (var direction in new[] { "向下", "向上", "交替" }) _direction.Items.Add(direction);
        _direction.SelectedIndex = AppController.Settings.ScrollDirection switch { "Up" => 1, "Alternate" => 2, _ => 0 };
        ViewFactory.Shell(this);
        Content = ViewFactory.Frame(this, new Grid { Margin = new Thickness(12), Children = { ViewFactory.Tabs(("窗口切换", BuildSwitchCard()), ("内容滚动", BuildScrollCard())) } }, "切换与滚动");
        _switchRun.Requested += start => { if (start) StartSwitching(); else _automation.StopSwitching(); };
        _scrollRun.Requested += start => { if (start) StartScrolling(); else _automation.StopScrolling(); };
        _scrollPicker.Click += (_, _) => SelectScrollApp();
        _mode.SelectionChanged += (_, _) => PersistOptions(); _direction.SelectionChanged += (_, _) => PersistOptions();
        foreach (var box in new[] { _noRepeat, _autoActivate, _randomPause, _randomRows })
        { box.Checked += (_, _) => PersistOptions(); box.Unchecked += (_, _) => PersistOptions(); }
        foreach (var box in new[] { _minSwitch, _maxSwitch, _minScroll, _maxScroll }) box.LostFocus += (_, _) => PersistOptions();
        _rows.ValueChanged += (_, _) => PersistOptions();
        _automation.Changed += AutomationChanged;
        _ready = true; Synchronize(); UpdateRowsLabel();
        Closed += (_, _) => { PersistOptions(); _automation.Changed -= AutomationChanged; };
    }

    private UIElement BuildSwitchCard()
    {
        var panel = new StackPanel();
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(ViewFactory.Action("选择应用", (_, _) => SelectSwitchApps(), true)); top.Children.Add(_selectedCount); panel.Children.Add(top);
        panel.Children.Add(ViewFactory.FieldRow("策略", _mode));
        panel.Children.Add(ViewFactory.FieldRow("间隔(秒)", _minSwitch, new TextBlock { Text = "至", Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, _maxSwitch));
        panel.Children.Add(_noRepeat); panel.Children.Add(_switchRun); panel.Children.Add(_switchStatus);
        return ViewFactory.Card(panel);
    }

    private UIElement BuildScrollCard()
    {
        var panel = new StackPanel();
        var target = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        target.Children.Add(new TextBlock { Text = "滚动应用", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        target.Children.Add(_scrollPicker); panel.Children.Add(target);
        panel.Children.Add(ViewFactory.FieldRow("方向", _direction));
        panel.Children.Add(ViewFactory.FieldRow("间隔(秒)", _minScroll, new TextBlock { Text = "至", Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, _maxScroll));
        var rows = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        rows.Children.Add(new TextBlock { Text = "滚动行数", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        rows.Children.Add(_rows); rows.Children.Add(_rowsValue); panel.Children.Add(rows);
        panel.Children.Add(_randomRows); panel.Children.Add(_autoActivate); panel.Children.Add(_randomPause);
        panel.Children.Add(_scrollRun); panel.Children.Add(_scrollStatus);
        return ViewFactory.Card(panel);
    }

    private void SelectSwitchApps()
    {
        var picker = new WindowPickerWindow(this, true, _automation.SwitchTargets.Select(t => t.Handle));
        if (picker.ShowDialog() == true) _automation.SetSwitchTargets(picker.SelectedTargets);
    }

    private void SelectScrollApp()
    {
        var picker = new WindowPickerWindow(this, false, _automation.ScrollTarget is { } target ? new[] { target.Handle } : Array.Empty<nint>());
        if (picker.ShowDialog() == true) _automation.SetScrollTarget(picker.SelectedTargets.FirstOrDefault());
    }

    private void AutomationChanged(object? sender, EventArgs args) => Synchronize();
    private void Synchronize()
    {
        _switchRun.IsRunning = _automation.IsSwitching; _scrollRun.IsRunning = _automation.IsScrolling;
        _switchStatus.Text = _automation.SwitchStatus; _scrollStatus.Text = _automation.ScrollStatus;
        _selectedCount.Text = $"已选 {_automation.SwitchTargets.Count} 个应用";
        var title = _automation.ScrollTarget?.DisplayName ?? "选择应用";
        _scrollPicker.Content = new TextBlock { Text = title, MaxWidth = 188, TextTrimming = TextTrimming.CharacterEllipsis }; _scrollPicker.ToolTip = title;
    }

    private static bool TryRange(TextBox minimum, TextBox maximum, int lower, int upper, out int min, out int max)
    {
        min = max = 0;
        return int.TryParse(minimum.Text, out min) && int.TryParse(maximum.Text, out max) && min >= lower && max <= upper && min <= max;
    }

    private void StartSwitching()
    {
        if (!TryRange(_minSwitch, _maxSwitch, 10, 180, out _, out _)) { _switchStatus.Text = "切换间隔请输入 10–180 秒，最小值不能大于最大值。"; return; }
        PersistOptions(); _automation.StartSwitching(SwitchOptions.FromSettings(AppController.Settings));
    }

    private void StartScrolling()
    {
        if (!TryRange(_minScroll, _maxScroll, 1, 600, out _, out _)) { _scrollStatus.Text = "滚动间隔请输入 1–600 秒，最小值不能大于最大值。"; return; }
        PersistOptions(); _automation.StartScrolling(ScrollOptions.FromSettings(AppController.Settings));
    }

    private void UpdateRowsLabel() => _rowsValue.Text = _randomRows.IsChecked == true ? $"1–{_rows.Value:0} 行" : $"{_rows.Value:0} 行";
    private void PersistOptions()
    {
        if (!_ready) return;
        var settings = AppController.Settings;
        if (TryRange(_minSwitch, _maxSwitch, 10, 180, out var switchMin, out var switchMax)) { settings.SwitchMinSeconds = switchMin; settings.SwitchMaxSeconds = switchMax; }
        if (TryRange(_minScroll, _maxScroll, 1, 600, out var scrollMin, out var scrollMax)) { settings.ScrollMinSeconds = scrollMin; settings.ScrollMaxSeconds = scrollMax; }
        settings.SwitchMode = _mode.SelectedIndex switch { 1 => "Ordered", 2 => "Manual", _ => "Random" }; settings.PreferDifferentWindow = _noRepeat.IsChecked == true;
        settings.ScrollDirection = _direction.SelectedIndex switch { 1 => "Up", 2 => "Alternate", _ => "Down" }; settings.ScrollRows = (int)_rows.Value;
        settings.ScrollRandomRows = _randomRows.IsChecked == true; settings.ScrollRandomPause = _randomPause.IsChecked == true; settings.ScrollMayActivateTarget = _autoActivate.IsChecked == true;
        UpdateRowsLabel(); _ = AppController.SaveSettingsAsync();
    }

    public static void SwitchNextGlobal() { if (!AppController.Automation.SwitchNext()) AppController.OpenTool("switcher"); }
    public static void ToggleAutomationGlobal()
    {
        if (AppController.Automation.IsSwitching) AppController.Automation.StopSwitching();
        else if (!AppController.StartSwitching()) AppController.OpenTool("switcher");
    }
    public static void ToggleReadingGlobal()
    {
        if (AppController.Automation.IsScrolling) AppController.Automation.StopScrolling();
        else if (!AppController.StartScrolling()) AppController.OpenTool("switcher");
    }
    public static void EmergencyStopAll() => AppController.Automation.StopAll();
}
