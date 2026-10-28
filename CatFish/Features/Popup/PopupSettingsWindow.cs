using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows;
using CatFish.Core;
using CatFish.UI;

namespace CatFish.Features.Popup;

public sealed class PopupSettingsWindow : Window
{
    private static PopupSettingsWindow? _active;
    private readonly ComboBox _corner = new() { Width = 110 };
    private readonly ComboBox _monitor = new() { Width = 160, DisplayMemberPath = nameof(MonitorBounds.Name) };
    private readonly TextBox _width = new() { Width = 60 };
    private readonly TextBox _height = new() { Width = 60 };
    private readonly ToggleButton _aspectLock = new() { Content = "🔗", Width = 32, Height = 28, FontSize = 15, IsChecked = AppController.Settings.PopupAspectRatioLocked, Margin = new Thickness(0, 0, 15, 8), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly ComboBox _backgroundChoice = new() { ItemsSource = PopupBackgrounds.Items, DisplayMemberPath = nameof(PopupBackground.Name) };
    private readonly Image _backgroundPreview = new() { Height = 180, Stretch = Stretch.Uniform, Margin = new Thickness(0, 12, 0, 10) };
    private double _aspectRatio;
    private bool _updatingDimensions;
    private readonly TextBox _hideAfter = new() { Width = 60 };
    private readonly TextBox _frequency = new() { Width = 60 };
    private readonly Slider _opacity = new() { Minimum = 50, Maximum = 100, Value = AppController.Settings.PopupOpacity * 100 };
    private readonly Slider _gradient = new() { Minimum = 0, Maximum = 120, Value = AppController.Settings.PopupGradientHeight };
    private readonly Slider _sideGradient = new() { Minimum = 0, Maximum = 120, Value = AppController.Settings.PopupSideMaskWidth };
    private readonly Slider _videoOpacity = new() { Minimum = 0, Maximum = 100, Value = AppController.Settings.PopupVideoOpacity * 100 };
    private readonly Slider _blur = new() { Minimum = 0, Maximum = 18, Value = AppController.Settings.PopupBlurRadius };
    private readonly CheckBox _locked = new() { Content = "锁定广告位置", IsChecked = AppController.Settings.PopupLocked };
    private readonly CheckBox _closeApplication = new() { Content = "关闭广告时，同时关闭所选应用", IsChecked = AppController.Settings.PopupCloseSelectedApplication,
        ToolTip = "默认关闭。勾选后向所选窗口发送正常关闭请求，不强制结束进程；应用可提示保存或退至托盘。紧急恢复不会关闭应用。" };
    private readonly CheckBox _auto = new() { Content = "定时显示" };
    private readonly TextBlock _status = ViewFactory.Status();
    private readonly System.Windows.Threading.DispatcherTimer _scheduleTimer = new();

    public PopupSettingsWindow()
    {
        _active = this; Title = "广告弹窗设置"; Width = 380; Height = 480; MinWidth = 360; MinHeight = 440;
        var settings = AppController.Settings;
        foreach (var name in new[] { "右下角", "左下角", "右上角", "左上角" }) _corner.Items.Add(name);
        _corner.SelectedIndex = settings.PopupCorner switch { "BottomLeft" => 1, "TopRight" => 2, "TopLeft" => 3, _ => 0 };
        _corner.SelectionChanged += (_, _) => settings.PopupUseCustomPosition = false;
        _width.Text = settings.PopupWidth.ToString(); _height.Text = settings.PopupHeight.ToString();
        _aspectRatio = settings.PopupWidth / (double)(settings.PopupHeight - PopupLayout.TitleBarHeight);
        _backgroundChoice.SelectedItem = PopupBackgrounds.Items.FirstOrDefault(background => background.Id == settings.PopupBackground) ?? PopupBackgrounds.Items[0];
        _backgroundPreview.Source = PopupBackgrounds.Load(settings.PopupBackground);
        _hideAfter.Text = settings.PopupAutoHideSeconds.ToString(); _frequency.Text = settings.PopupFrequencySeconds.ToString();
        _monitor.ItemsSource = MonitorService.GetMonitors(); _monitor.SelectedIndex = Math.Clamp(settings.PopupMonitorIndex, 0, Math.Max(0, _monitor.Items.Count - 1));
        ViewFactory.Shell(this);
        var layout = new DockPanel { Margin = new Thickness(12) };
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var actions = new WrapPanel();
        actions.Children.Add(ViewFactory.Action("保存", (_, _) => SaveSettings()));
        actions.Children.Add(ViewFactory.Action("显示广告", (_, _) => { if (SaveSettings()) AdvertisementPopupWindow.ShowGlobal(); }, true));
        actions.Children.Add(_auto); footer.Children.Add(actions); footer.Children.Add(_status);
        var position = new StackPanel();
        var row = new WrapPanel(); row.Children.Add(Field("位置", _corner)); row.Children.Add(Field("显示器", _monitor));
        position.Children.Add(row);
        var dimensions = new StackPanel { Orientation = Orientation.Horizontal };
        dimensions.Children.Add(Field("宽度", _width)); dimensions.Children.Add(_aspectLock); dimensions.Children.Add(Field("高度", _height)); position.Children.Add(dimensions);
        position.Children.Add(ViewFactory.Action("恢复 1:1 背景", (_, _) => RestoreSquareSize()));
        var timing = new WrapPanel(); timing.Children.Add(Field("自动隐藏 / 秒", _hideAfter)); timing.Children.Add(Field("弹出间隔 / 秒", _frequency)); position.Children.Add(timing);
        position.Children.Add(ViewFactory.Muted("自动隐藏填 0 表示常驻；嵌入播放时暂停隐藏。")); position.Children.Add(_locked);
        position.Children.Add(_closeApplication);
        var style = new StackPanel(); style.Children.Add(SliderLine("背景 / 遮罩不透明度", _opacity)); style.Children.Add(SliderLine("视频不透明度", _videoOpacity));
        style.Children.Add(SliderLine("上下遮罩高度", _gradient)); style.Children.Add(SliderLine("左右遮罩宽度", _sideGradient)); style.Children.Add(SliderLine("背景模糊半径", _blur));
        style.Children.Add(ViewFactory.Muted("四边遮罩覆盖视频；降低视频不透明度可透出广告背景。滑块实时预览，点击保存保留。"));
        var backgrounds = new StackPanel(); backgrounds.Children.Add(_backgroundChoice); backgrounds.Children.Add(_backgroundPreview);
        backgrounds.Children.Add(ViewFactory.Muted("五款背景统一为 1:1 正方形；解锁宽高后整图随窗口缩放，不裁剪。也可在顶部点击「背景」轮换。"));
        layout.Children.Add(ViewFactory.Tabs(("位置与时间", ViewFactory.Card(position)), ("遮罩样式", ViewFactory.Card(style)), ("背景风格", ViewFactory.Card(backgrounds))));
        Content = ViewFactory.Frame(this, layout, Title);
        UpdateAspectButton();
        _aspectLock.Checked += (_, _) => { if (Read(_width, PopupLayout.MinWidth, 900, out var width) && Read(_height, PopupLayout.MinHeight, 700, out var height)) _aspectRatio = width / (double)(height - PopupLayout.TitleBarHeight); settings.PopupAspectRatioLocked = true; UpdateAspectButton(); };
        _aspectLock.Unchecked += (_, _) => { settings.PopupAspectRatioLocked = false; UpdateAspectButton(); };
        _closeApplication.Checked += (_, _) => settings.PopupCloseSelectedApplication = true;
        _closeApplication.Unchecked += (_, _) => settings.PopupCloseSelectedApplication = false;
        _width.TextChanged += (_, _) => UpdateDimensions(true); _height.TextChanged += (_, _) => UpdateDimensions(false);
        _backgroundChoice.SelectionChanged += (_, _) =>
        {
            if (_backgroundChoice.SelectedItem is not PopupBackground background) return;
            settings.PopupBackground = background.Id; _backgroundPreview.Source = PopupBackgrounds.Load(background.Id);
            AdvertisementPopupWindow.ApplyOpenSettings(); _status.Text = "背景已预览，点击保存保留。";
        };
        foreach (var slider in new[] { _opacity, _videoOpacity, _gradient, _sideGradient, _blur }) slider.ValueChanged += (_, _) => PreviewAppearance();
        _scheduleTimer.Tick += (_, _) => AdvertisementPopupWindow.ShowGlobal();
        _auto.Checked += (_, _) => { if (!SaveSettings()) { _auto.IsChecked = false; return; } _scheduleTimer.Interval = TimeSpan.FromSeconds(settings.PopupFrequencySeconds); _scheduleTimer.Start(); };
        _auto.Unchecked += (_, _) => _scheduleTimer.Stop();
        Closed += (_, _) => { _scheduleTimer.Stop(); if (_active == this) _active = null; };
    }

    private static UIElement Field(string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 15, 8) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = ViewFactory.MutedBrush, Margin = new Thickness(0, 0, 0, 4) }); panel.Children.Add(control); return panel;
    }

    private static UIElement SliderLine(string label, Slider slider)
    {
        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 6) };
        var value = new TextBlock { Width = 28, Text = $"{slider.Value:0}", TextAlignment = TextAlignment.Right };
        slider.ValueChanged += (_, _) => value.Text = $"{slider.Value:0}";
        DockPanel.SetDock(value, Dock.Right); row.Children.Add(value); row.Children.Add(new TextBlock { Text = label, Width = 110 }); row.Children.Add(slider); return row;
    }

    private bool SaveSettings()
    {
        if (!Read(_width, PopupLayout.MinWidth, 900, out var width) || !Read(_height, PopupLayout.MinHeight, 700, out var height)
            || !Read(_hideAfter, 0, 600, out var hide) || !Read(_frequency, 10, 3600, out var frequency))
        { _status.Text = "宽 288–900，高 192–700，隐藏 0–600 秒，间隔 10–3600 秒。"; return false; }
        var settings = AppController.Settings;
        settings.PopupLayoutVersion = PopupLayout.Version; settings.PopupWidth = width; settings.PopupHeight = height;
        settings.PopupAspectRatioLocked = _aspectLock.IsChecked == true;
        settings.PopupAutoHideSeconds = hide; settings.PopupFrequencySeconds = frequency;
        settings.PopupCorner = _corner.SelectedIndex switch { 1 => "BottomLeft", 2 => "TopRight", 3 => "TopLeft", _ => "BottomRight" };
        settings.PopupMonitorIndex = Math.Max(0, _monitor.SelectedIndex); settings.PopupLocked = _locked.IsChecked == true;
        settings.PopupCloseSelectedApplication = _closeApplication.IsChecked == true;
        settings.PopupUseCustomPosition = false;
        PreviewAppearance(); _ = AppController.SaveSettingsAsync(); _status.Text = "设置已保存。"; return true;
    }
    private void UpdateAspectButton()
    {
        var locked = _aspectLock.IsChecked == true;
        _aspectLock.Content = locked ? "🔗" : "↔";
        _aspectLock.ToolTip = locked ? "背景区域比例已锁定，标题栏不参与联动；点击解锁" : "自由调整宽高；点击锁定当前背景比例";
    }
    internal void RestoreSquareSize()
    {
        var width = int.TryParse(_width.Text, out var parsed) ? parsed : AppController.Settings.PopupWidth;
        var size = PopupLayout.SquareSize(width);
        _updatingDimensions = true;
        try
        {
            _width.Text = size.Width.ToString(); _height.Text = size.Height.ToString();
            _aspectLock.IsChecked = true; _aspectRatio = 1;
            var settings = AppController.Settings;
            settings.PopupWidth = size.Width; settings.PopupHeight = size.Height; settings.PopupAspectRatioLocked = true;
            AdvertisementPopupWindow.ApplyOpenSettings(); _status.Text = "背景已恢复 1:1，标题栏另占 30 像素；点击保存保留。";
        }
        finally { _updatingDimensions = false; }
    }
    private void UpdateDimensions(bool widthChanged)
    {
        if (_updatingDimensions) return;
        if (!Read(_width, PopupLayout.MinWidth, 900, out var width) || !Read(_height, PopupLayout.MinHeight, 700, out var height)) return;
        _updatingDimensions = true;
        try
        {
            if (_aspectLock.IsChecked == true)
            {
                if (widthChanged)
                {
                    var suggestedHeight = (int)Math.Round(width / _aspectRatio, MidpointRounding.AwayFromZero) + PopupLayout.TitleBarHeight;
                    height = Math.Clamp(suggestedHeight, PopupLayout.MinHeight, 700);
                    if (height != suggestedHeight) width = Math.Clamp((int)Math.Round((height - PopupLayout.TitleBarHeight) * _aspectRatio), PopupLayout.MinWidth, 900);
                }
                else
                {
                    var suggestedWidth = (int)Math.Round((height - PopupLayout.TitleBarHeight) * _aspectRatio, MidpointRounding.AwayFromZero);
                    width = Math.Clamp(suggestedWidth, PopupLayout.MinWidth, 900);
                    if (width != suggestedWidth) height = Math.Clamp((int)Math.Round(width / _aspectRatio) + PopupLayout.TitleBarHeight, PopupLayout.MinHeight, 700);
                }
                _width.Text = width.ToString(); _height.Text = height.ToString();
            }
            var settings = AppController.Settings; settings.PopupWidth = width; settings.PopupHeight = height;
            AdvertisementPopupWindow.ApplyOpenSettings();
        }
        finally { _updatingDimensions = false; }
    }
    private void PreviewAppearance()
    {
        var settings = AppController.Settings;
        settings.PopupOpacity = _opacity.Value / 100; settings.PopupGradientHeight = (int)_gradient.Value; settings.PopupBlurRadius = (int)_blur.Value;
        settings.PopupSideMaskWidth = (int)_sideGradient.Value; settings.PopupVideoOpacity = _videoOpacity.Value / 100;
        AdvertisementPopupWindow.ApplyOpenSettings(); _status.Text = "样式已预览，点击保存保留。";
    }
    private static bool Read(TextBox box, int min, int max, out int value) => int.TryParse(box.Text, out value) && value >= min && value <= max;
    public static void TogglePreviewGlobal() => AdvertisementPopupWindow.ToggleGlobal();
    internal static void RefreshBackgroundSelection()
    {
        if (_active is not null) _active._backgroundChoice.SelectedItem = PopupBackgrounds.Items.FirstOrDefault(background => background.Id == AppController.Settings.PopupBackground) ?? PopupBackgrounds.Items[0];
    }
    public static void EmergencyStop()
    {
        if (_active is not null) { _active._scheduleTimer.Stop(); _active._auto.IsChecked = false; }
        AdvertisementPopupWindow.CloseAll();
    }
}
