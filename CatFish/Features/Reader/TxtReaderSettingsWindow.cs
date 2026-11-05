using System.Globalization;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using CatFish.Core;
using CatFish.UI;
using Microsoft.Win32;
using ColorPopup = System.Windows.Controls.Primitives.Popup;

namespace CatFish.Features.Reader;

public sealed class TxtReaderSettingsWindow : Window
{
    private static readonly ControlTemplate SectionTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type GroupBox}">
          <Grid SnapsToDevicePixels="True">
            <Border Margin="0,7,0,0" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                    CornerRadius="4" Background="{TemplateBinding Background}"/>
            <Grid>
              <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
              <Border HorizontalAlignment="Left" Margin="10,0,0,0" Padding="4,0" Background="{TemplateBinding Background}">
                <ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/>
              </Border>
              <ContentPresenter Grid.Row="1" ContentSource="Content" Margin="{TemplateBinding Padding}"/>
            </Grid>
          </Grid>
        </ControlTemplate>
        """);
    private readonly TxtReaderService _reader;
    private readonly TxtReaderSettings _settings;
    private readonly TextBlock _file = ViewFactory.Muted("尚未打开文件");
    private readonly TextBlock _progress = ViewFactory.Muted("");
    private readonly TextBlock _status = ViewFactory.Status();
    private readonly ListBox _recent = new() { Height = 104 };
    private readonly Button _play, _stop;
    private bool _closed;
    private bool _syncing;
    private readonly List<Action> _refreshControls = new();
    private ColorPopup? _colorPopup;

    public TxtReaderSettingsWindow()
    {
        _reader = AppController.TxtReader; _settings = _reader.Settings;
        Title = "单行阅读 · CatFish"; Width = 430; Height = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ViewFactory.Shell(this);
        FontSize = 11;
        _play = CompactAction("开始", (_, _) => { AppController.ShowTxtReader(); _reader.Play(); });
        _stop = CompactAction("停止", (_, _) => _reader.Stop(), true);
        _play.Width = _stop.Width = 64;
        var recentRowStyle = new Style(typeof(ListBoxItem), (Style)Application.Current.FindResource(typeof(ListBoxItem)));
        recentRowStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 24d)); _recent.ItemContainerStyle = recentRowStyle;
        var root = new DockPanel { Margin = new Thickness(10, 4, 10, 8) };
        var footer = new DockPanel(); _status.Height = 28; _status.FontSize = 10; _status.Margin = new Thickness(0, 3, 6, 0);
        var links = new WrapPanel();
        links.Children.Add(CompactAction("主菜单", (_, _) => AppController.ReturnToMain(), true));
        links.Children.Add(CompactAction("快捷键", (_, _) => AppController.ShowFeature("hotkeys", () => new HotkeySettingsWindow()), true));
        DockPanel.SetDock(links, Dock.Right); footer.Children.Add(links); footer.Children.Add(_status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = ViewFactory.Tabs(("阅读", ReadingPage()), ("文字与浮窗", AppearancePage()));
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs) && _colorPopup is not null) _colorPopup.IsOpen = false; };
        root.Children.Add(tabs);
        Content = ViewFactory.Frame(this, root, "单行阅读");
        AllowDrop = true;
        PreviewDragOver += (_, e) => { e.Effects = IsTxtDrop(e) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) =>
        {
            if (IsTxtDrop(e)) await OpenAsync(((string[])e.Data.GetData(DataFormats.FileDrop))[0]);
            else _status.Text = "请拖入一个 .txt 文件。";
            e.Handled = true;
        };
        _reader.Changed += ReaderChanged;
        Loaded += async (_, _) =>
        {
            if (_reader.Document is null && !string.IsNullOrWhiteSpace(_settings.LastFile))
            {
                var record = _settings.RecentFiles.FirstOrDefault(r => r.Path.Equals(_settings.LastFile, StringComparison.OrdinalIgnoreCase));
                await OpenAsync(_settings.LastFile, record?.Encoding ?? "Auto");
            }
            else if (_reader.Document is not null) AppController.ShowTxtReader();
            if (!_closed) { AppController.SetTxtReaderOutline(true); UpdateState(); }
        };
        LocationChanged += (_, _) => { if (_colorPopup is not null) _colorPopup.IsOpen = false; };
        Closed += (_, _) => { _closed = true; if (_colorPopup is not null) _colorPopup.IsOpen = false; _reader.Changed -= ReaderChanged; AppController.SetTxtReaderOutline(false); _ = _reader.PersistAsync(); };
        UpdateState();
    }

    private StackPanel ReadingPage()
    {
        var page = Page();
        var file = new StackPanel();
        file.Children.Add(_file); _file.Margin = new Thickness(0, 0, 0, 3);
        _file.TextWrapping = TextWrapping.NoWrap; _file.TextTrimming = TextTrimming.CharacterEllipsis;
        var openFile = ViewFactory.Action("打开 TXT", async (_, _) => await ChooseFileAsync()); openFile.Width = 64;
        file.Children.Add(Buttons(openFile,
            ViewFactory.Action("重新加载", async (_, _) => { if (_reader.Document is { } doc) await OpenAsync(doc.Path, SelectedEncoding); }, true),
            ViewFactory.Action("显示 / 关闭浮窗", (_, _) => AppController.ToggleTxtReader(), true)));
        file.ToolTip = "支持拖入 TXT；20 MB 以上按需分页，文件上限 32 MB。";
        page.Children.Add(Group("选择文本", file));
        var controls = new StackPanel(); controls.Children.Add(_progress);
        controls.Children.Add(Buttons(ViewFactory.Action("上一页", (_, _) => _reader.Previous(), true), ViewFactory.Action("下一页", (_, _) => _reader.Next(), true),
            ViewFactory.Action("回退 5 段", (_, _) => { for (var i = 0; i < 5; i++) _reader.Previous(); }, true), ViewFactory.Action("返回开头", (_, _) => _reader.Seek(0), true)));
        var percent = new TextBox { Text = "50", Width = 38, ToolTip = "阅读百分比，范围 0～100" };
        var offset = new TextBox { Text = "0", Width = 61, ToolTip = "原文字符位置" };
        controls.Children.Add(Buttons(new TextBlock { Text = "% ", VerticalAlignment = VerticalAlignment.Center }, percent, ViewFactory.Action("跳转", (_, _) =>
        { if (double.TryParse(percent.Text, out var value) && double.IsFinite(value) && value >= 0 && value <= 100) _reader.SeekPercent(value); else _status.Text = "请输入 0～100 的百分比。"; }, true),
            new TextBlock { Text = "位置 ", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, offset, ViewFactory.Action("定位", (_, _) =>
        { if (int.TryParse(offset.Text, out var value) && value >= 0) _reader.Seek(value); else _status.Text = "请输入非负的原文字符偏移。"; }, true)));
        controls.Children.Add(TextLocator());
        page.Children.Add(Group("阅读进度", controls));
        var playback = new StackPanel();
        playback.Children.Add(Buttons(_play, _stop));
        var mode = Choice("显示方式", new[] { ("静态替换", "Static"), ("平滑滑动", "Scroll") }, () => _settings.Mode, value => _settings.Mode = value, true, 52);
        var staticSpeed = Number("每屏秒数", () => _settings.SecondsPerPage, 1, 30, value => _settings.SecondsPerPage = value, repaginate: false, labelWidth: 52);
        var scrollSpeed = Number("速度 px/s", () => _settings.PixelsPerSecond, 10, 200, value => _settings.PixelsPerSecond = value, repaginate: false, labelWidth: 52);
        var speed = new Grid(); speed.Children.Add(staticSpeed); speed.Children.Add(scrollSpeed);
        playback.Children.Add(Pair(mode, speed));
        _refreshControls.Add(() => { staticSpeed.Visibility = _settings.Mode == "Static" ? Visibility.Visible : Visibility.Collapsed; scrollSpeed.Visibility = _settings.Mode == "Scroll" ? Visibility.Visible : Visibility.Collapsed; });
        playback.Children.Add(Pair(Choice("阅读完成", new[] { ("停止保留末段", "Stop"), ("循环至开头", "Loop"), ("收起浮窗", "Hide") }, () => _settings.EndBehavior, value => _settings.EndBehavior = value, labelWidth: 52),
            Check("手动翻页暂停自动", () => _settings.PauseOnManualNavigation, value => _settings.PauseOnManualNavigation = value)));
        page.Children.Add(Group("自动阅读", playback));
        var history = new StackPanel(); history.Children.Add(_recent);
        _recent.MouseDoubleClick += async (_, _) => { if (_recent.SelectedItem is TxtReadingRecord record) await OpenAsync(record.Path, record.Encoding); };
        history.Children.Add(Buttons(ViewFactory.Action("继续选中文件", async (_, _) => { if (_recent.SelectedItem is TxtReadingRecord record) await OpenAsync(record.Path, record.Encoding); }, true),
            ViewFactory.Action("重新定位", async (_, _) => await RelocateAsync(), true),
            ViewFactory.Action("移除记录", (_, _) => RemoveHistory(false), true), ViewFactory.Action("清空历史", (_, _) => RemoveHistory(true), true)));
        page.Children.Add(Group("最近文件", history)); return page;
    }

    private UIElement TextLocator()
    {
        var input = new TextBox { Name = "ReaderTextSearchInput", Margin = new Thickness(0, 1, 4, 1), ToolTip = "输入原文中的连续文字，限 10～30 字；按 Enter 定位到全文首个匹配处。" };
        System.Windows.Automation.AutomationProperties.SetName(input, "文字内容定位（10～30 字）");
        void Locate()
        {
            _reader.TrySeekText(input.Text, out var message); _status.Text = message;
        }
        bool Fits(string inserted) => new StringInfo(input.Text.Remove(input.SelectionStart, input.SelectionLength).Insert(input.SelectionStart, inserted)).LengthInTextElements <= 30;
        input.PreviewTextInput += (_, e) =>
        {
            if (!Fits(e.Text)) { e.Handled = true; _status.Text = "文字定位最多输入 30 字。"; }
        };
        DataObject.AddPastingHandler(input, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.UnicodeText) is string pasted && !Fits(pasted))
            { e.CancelCommand(); _status.Text = "文字定位最多输入 30 字，请缩短粘贴内容。"; }
        });
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Locate(); e.Handled = true; } };
        var locate = ViewFactory.Action("定位", (_, _) => Locate(), true); locate.Name = "ReaderTextLocateButton";
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(input); Grid.SetColumn(locate, 1); row.Children.Add(locate);
        var content = new StackPanel(); content.Children.Add(Field("文字定位", row, 52));
        content.Children.Add(ViewFactory.Muted("输入 10～30 字，按原文查找首个匹配处。"));
        return content;
    }

    private StackPanel AppearancePage()
    {
        var page = Page(); var layout = new StackPanel();
        layout.Children.Add(Number("最大字数", () => _settings.MaxCharacters, 8, 64, value => _settings.MaxCharacters = (int)value));
        var presets = new UniformGrid { Columns = 5, Margin = new Thickness(0, 2, 0, 4) };
        foreach (var count in new[] { 16, 24, 32, 48, 64 })
        {
            var preset = ViewFactory.Action(count.ToString(), (_, _) => { _settings.MaxCharacters = count; ApplyChange(); }, true);
            preset.Height = 28; presets.Children.Add(preset);
        }
        layout.Children.Add(presets);
        layout.Children.Add(Pair(Check("标点 / 词边界分页", () => _settings.PreferNaturalBreaks, value => _settings.PreferNaturalBreaks = value, true),
            Check("合并空格与换行", () => _settings.CompressWhitespace, value => _settings.CompressWhitespace = value, true)));
        page.Children.Add(Group("分页", layout));
        var font = new StackPanel();
        var fontBox = new ComboBox { Name = "ReaderFontFamilyInput" };
        ReaderInputWheel.Attach(fontBox);
        foreach (var (label, family) in new[] { ("微软雅黑", "Microsoft YaHei UI"), ("宋体", "SimSun"), ("黑体", "SimHei"), ("楷体", "KaiTi"), ("仿宋", "FangSong"), ("等线", "DengXian") })
            fontBox.Items.Add(new ComboBoxItem { Content = label, Tag = family });
        // Keep an existing custom font visible until the user chooses one of the common fonts.
        var currentFont = new TextBlock { Margin = new Thickness(7, 4, 26, 4), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis };
        var fontEditor = new Grid(); fontEditor.Children.Add(fontBox); fontEditor.Children.Add(currentFont);
        font.Children.Add(Pair(Field("字体", fontEditor, 34), NumericField("字号", () => _settings.FontSize, 10, 72, value => _settings.FontSize = value), 104));
        fontBox.SelectionChanged += (_, _) => { if (!_syncing && fontBox.SelectedItem is ComboBoxItem { Tag: string fontName }) { _settings.FontFamily = fontName; ApplyChange(); } };
        _refreshControls.Add(() =>
        {
            fontBox.SelectedItem = fontBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == _settings.FontFamily);
            currentFont.Text = _settings.FontFamily; currentFont.Visibility = fontBox.SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
        });
        font.Children.Add(Pair(Choice("对齐", new[] { ("左对齐", "Left"), ("居中", "Center"), ("右对齐", "Right") }, () => _settings.Alignment, value => _settings.Alignment = value, labelWidth: 34),
            Check("粗体", () => _settings.Bold, value => _settings.Bold = value, true), 94));
        var swatch = new Border { Width = 16, Height = 13, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 5, 0) };
        _refreshControls.Add(() => swatch.Background = new SolidColorBrush(CurrentTextColor()));
        var palette = new ReaderColorPalette();
        var chooseColor = ViewFactory.Action("选择颜色", (_, _) =>
        {
            if (_colorPopup is null) return;
            if (!_colorPopup.IsOpen) palette.SetColor(CurrentTextColor());
            _colorPopup.IsOpen = !_colorPopup.IsOpen;
        }, true);
        chooseColor.Name = "ReaderColorPickerButton";
        var paletteCaption = new StackPanel { Orientation = Orientation.Horizontal }; paletteCaption.Children.Add(swatch); paletteCaption.Children.Add(new TextBlock { Text = "选择颜色" }); chooseColor.Content = paletteCaption;
        _colorPopup = new ColorPopup { PlacementTarget = chooseColor, Placement = PlacementMode.Bottom, VerticalOffset = 4,
            Child = palette, AllowsTransparency = true, StaysOpen = false, Focusable = false };
        _colorPopup.Closed += (_, _) => _ = _reader.PersistAsync();
        palette.ColorSelected += (selected, committed) =>
        {
            _settings.TextColor = $"#{selected.R:X2}{selected.G:X2}{selected.B:X2}";
            ApplyChange(false, false); if (committed) _colorPopup.IsOpen = false;
        };
        font.Children.Add(Field("文字颜色", Buttons(chooseColor)));
        page.Children.Add(Group("字体样式", font));
        var opacity = new StackPanel();
        opacity.Children.Add(Pair(Number("文字", () => _settings.TextOpacity * 100, 0, 100, value => _settings.TextOpacity = value / 100, repaginate: false, labelWidth: 30),
            Number("底板", () => _settings.BackgroundOpacity * 100, 0, 100, value => _settings.BackgroundOpacity = value / 100, repaginate: false, labelWidth: 30)));
        opacity.ToolTip = "文字和底板分别设置不透明度，范围 0～100%。";
        opacity.Children.Add(ViewFactory.Action("恢复可见文字及默认位置", (_, _) => AppController.RestoreTxtReader(), true));
        page.Children.Add(Group("透明度", opacity)); AddPositionGroups(page); return page;
    }

    private void AddPositionGroups(StackPanel page)
    {
        var position = new StackPanel();
        position.Children.Add(Pair(Check("保持置顶", () => _settings.Topmost, value => _settings.Topmost = value),
            Check("锁定位置", () => _settings.Locked, value => _settings.Locked = value)));
        position.Children.Add(Pair(Check("鼠标滚轮翻页", () => _settings.MouseWheelPaging, value => _settings.MouseWheelPaging = value),
            Check("启动时显示单行阅读", () => _settings.ShowOnStartup, value => _settings.ShowOnStartup = value)));
        page.Children.Add(Group("浮窗行为", position)); position = new StackPanel();
        position.Children.Add(Pair(Number("宽度", () => _settings.Width, 160, 2400, value => _settings.Width = value, labelWidth: 30),
            Number("高度", () => _settings.Height, 24, 180, value => _settings.Height = value, labelWidth: 30)));
        position.Children.Add(Buttons(ViewFactory.Action("底部居中", (_, _) => { AppController.ShowTxtReader(); AppController.ReaderWindow?.SetDefaultPosition(); }, true),
            ViewFactory.Action("恢复位置与样式", (_, _) => AppController.RestoreTxtReader(), true)));
        position.ToolTip = "未锁定时，整个浮窗都可拖动。悬停显示菜单，位置会自动保存。";
        page.Children.Add(Group("尺寸与位置", position));
    }

    private string SelectedEncoding => _settings.RecentFiles.FirstOrDefault(record => record.Path == _reader.Document?.Path)?.Encoding ?? "Auto";
    private static StackPanel Page() => new() { Margin = new Thickness(0, 4, 0, 0) };
    private static GroupBox Group(string title, UIElement content)
    {
        Compact(content);
        return new GroupBox { Header = title, Content = content, Template = SectionTemplate, Background = (Brush)Application.Current.Resources["CanvasBrush"], BorderBrush = new SolidColorBrush(Color.FromRgb(196, 212, 204)), BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 5), Margin = new Thickness(0, 1, 0, 3), Foreground = (Brush)Application.Current.Resources["TextBrush"], FontSize = 11 };
    }
    private static void Compact(UIElement element)
    {
        if (element is Button button) { button.Padding = new Thickness(7, 3, 7, 3); button.Margin = new Thickness(0, 1, 4, 1); button.FontSize = 11; }
        if (element is TextBlock text) text.FontSize = 11;
        if (element is TextBox box) box.Padding = new Thickness(4, 1, 4, 1);
        if (element is StackPanel stack && stack.Orientation == Orientation.Horizontal) stack.Margin = new Thickness(0, 1, 0, 3);
        if (element is Panel panel) foreach (UIElement child in panel.Children) Compact(child);
    }
    private static Button CompactAction(string text, RoutedEventHandler click, bool secondary = false)
    { var button = ViewFactory.Action(text, click, secondary); Compact(button); return button; }
    private static WrapPanel Buttons(params UIElement[] buttons) { var row = new WrapPanel { Margin = new Thickness(0, 1, 0, 1) }; foreach (var button in buttons) row.Children.Add(button); return row; }

    private static Grid Pair(UIElement first, UIElement second, double? rightWidth = null)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = rightWidth is double width ? new GridLength(width) : new GridLength(1, GridUnitType.Star) });
        if (first is FrameworkElement left) left.VerticalAlignment = VerticalAlignment.Center;
        if (second is FrameworkElement right) { right.Margin = new Thickness(right.Margin.Left + 8, right.Margin.Top, right.Margin.Right, right.Margin.Bottom); right.VerticalAlignment = VerticalAlignment.Center; }
        row.Children.Add(first); Grid.SetColumn(second, 1); row.Children.Add(second); return row;
    }
    private static Grid Field(string name, UIElement control, double labelWidth = 86)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 3) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(labelWidth) }); row.ColumnDefinitions.Add(new());
        row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }
    private UIElement NumericField(string name, Func<double> read, double min, double max, Action<double> update)
    {
        var stepper = new ReaderNumberStepper("ReaderFontSize", name, read, () => min, () => max,
            value => { if (_syncing || _closed) return; update(value); ApplyChange(); }, message => _status.Text = message);
        _refreshControls.Add(stepper.Refresh); return Field(name, stepper, 34);
    }
    private Color CurrentTextColor()
    {
        try { return (Color)ColorConverter.ConvertFromString(_settings.TextColor); }
        catch { return Color.FromRgb(41, 60, 53); }
    }

    private UIElement Number(string name, Func<double> read, double min, double max, Action<double> update, double step = 1, bool repaginate = true, double labelWidth = 86)
    {
        var initial = read();
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 3) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(labelWidth) }); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(48) });
        grid.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { Minimum = min, Maximum = max, Value = initial, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 8, 0) };
        var box = new TextBox { Text = initial.ToString("0.#", CultureInfo.CurrentCulture) }; Grid.SetColumn(slider, 1); Grid.SetColumn(box, 2);
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        grid.Children.Add(slider); grid.Children.Add(box);
        slider.ValueChanged += (_, _) => { if (_syncing) return; box.Text = slider.Value.ToString("0.#", CultureInfo.CurrentCulture); update(slider.Value); ApplyChange(repaginate); };
        void Adjust(int notches)
        {
            if (_syncing || _closed) return;
            var pending = double.TryParse(box.Text, out var value) && double.IsFinite(value) && value >= min && value <= max ? value : read();
            var next = Math.Clamp(pending + notches * step, min, max);
            box.Text = next.ToString("0.#", CultureInfo.CurrentCulture); slider.Value = next;
        }
        ReaderInputWheel.Attach(box, Adjust); ReaderInputWheel.Attach(slider, Adjust);
        _refreshControls.Add(() => { slider.Value = read(); if (!box.IsKeyboardFocusWithin) box.Text = read().ToString("0.#", CultureInfo.CurrentCulture); });
        void Commit()
        {
            if (double.TryParse(box.Text, out var value) && double.IsFinite(value) && value >= min && value <= max) slider.Value = value;
            else { box.Text = slider.Value.ToString("0.#", CultureInfo.CurrentCulture); _status.Text = $"{name}范围为 {min}～{max}。"; }
        }
        box.LostKeyboardFocus += (_, _) => Commit(); box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } }; return grid;
    }
    private UIElement Choice(string name, (string Name, string Value)[] choices, Func<string> read, Action<string> update, bool repaginate = false, double labelWidth = 86)
    {
        var combo = new ComboBox(); ReaderInputWheel.Attach(combo);
        foreach (var choice in choices) combo.Items.Add(new ComboBoxItem { Content = choice.Name, Tag = choice.Value });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == read());
        _refreshControls.Add(() => combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == read()));
        combo.SelectionChanged += (_, _) => { if (_syncing) return; if (combo.SelectedItem is ComboBoxItem item) { update((string)item.Tag); ApplyChange(repaginate); } };
        return Field(name, combo, labelWidth);
    }
    private UIElement Check(string name, Func<bool> read, Action<bool> update, bool repaginate = false)
    {
        var check = new CheckBox { Content = name, IsChecked = read(), Margin = new Thickness(0, 2, 6, 2) };
        _refreshControls.Add(() => check.IsChecked = read());
        check.Checked += (_, _) => { if (_syncing) return; update(true); ApplyChange(repaginate); }; check.Unchecked += (_, _) => { if (_syncing) return; update(false); ApplyChange(repaginate); }; return check;
    }
    private void ApplyChange(bool repaginate = true, bool persist = true) { if (_syncing) return; _reader.RefreshLayout(repaginate, persist); UpdateState(); }
    private void ReaderChanged(object? sender, EventArgs e) { if (!_closed) UpdateState(); }
    private void UpdateState()
    {
        _syncing = true;
        try { foreach (var refresh in _refreshControls) refresh(); } finally { _syncing = false; }
        var doc = _reader.Document;
        _file.Text = doc is null ? "选择或拖入 TXT 文件" : Path.GetFileName(doc.Path);
        _file.ToolTip = doc?.Path;
        _progress.Text = doc is null ? "尚未载入文本" : $"{doc.ElementIndex(_reader.Offset):N0} / {doc.TotalElements:N0} 字符 · {(double)doc.ElementIndex(_reader.Offset) / Math.Max(1, doc.TotalElements) * 100:0.0}%";
        _progress.ToolTip = $"原文字符位置：{_reader.Offset:N0}";
        _play.IsEnabled = doc is not null && !_reader.IsPlaying; _stop.IsEnabled = doc is not null;
        _status.Text = _reader.Status;
        var selectedPath = (_recent.SelectedItem as TxtReadingRecord)?.Path;
        _recent.ItemsSource = null; _recent.ItemsSource = _settings.RecentFiles.ToArray();
        _recent.SelectedItem = _recent.Items.Cast<TxtReadingRecord>().FirstOrDefault(r => r.Path == selectedPath);
    }

    private async Task ChooseFileAsync()
    {
        var dialog = new OpenFileDialog { Filter = "TXT 文本文件 (*.txt)|*.txt", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true) await OpenAsync(dialog.FileName);
    }
    private async Task<bool> OpenAsync(string path, string encoding = "Auto")
    {
        var success = await _reader.OpenAsync(path, encoding, record =>
        {
            if (_closed) return null;
            var answer = MessageBox.Show(this, "文本已更新。\n选择“是”使用上次字符位置（超出长度时回到末尾）；\n选择“否”从头开始；选择“取消”保留当前内容。", "文本已更新", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
            return answer == MessageBoxResult.Cancel ? null : answer == MessageBoxResult.Yes ? record.Offset : 0;
        });
        if (success && !_closed)
        {
            AppController.ShowTxtReader(); AppController.SetTxtReaderOutline(true);
        }
        return success;
    }
    private async Task RelocateAsync()
    {
        if (_recent.SelectedItem is not TxtReadingRecord record) { _status.Text = "请先选择需要重新定位的阅读记录。"; return; }
        var dialog = new OpenFileDialog { Filter = "TXT 文本文件 (*.txt)|*.txt", FileName = Path.GetFileName(record.Path) };
        if (dialog.ShowDialog(this) != true) return;
        var offset = record.Offset;
        if (await OpenAsync(dialog.FileName, record.Encoding))
        {
            _reader.Seek(offset);
            if (!string.Equals(record.Path, _reader.Document?.Path, StringComparison.OrdinalIgnoreCase)) _settings.RecentFiles.Remove(record);
            await _reader.PersistAsync(); UpdateState();
        }
    }
    private void RemoveHistory(bool all)
    {
        if (all) _settings.RecentFiles.Clear();
        else if (_recent.SelectedItem is TxtReadingRecord record) _settings.RecentFiles.Remove(record);
        if (!_settings.RecentFiles.Any(r => r.Path == _settings.LastFile)) _settings.LastFile = null;
        _ = _reader.PersistAsync(); UpdateState();
    }
    private static bool IsTxtDrop(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) &&
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && string.Equals(Path.GetExtension(files[0]), ".txt", StringComparison.OrdinalIgnoreCase);
}
