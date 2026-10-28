using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CatFish.Core;
using CatFish.UI;

namespace CatFish.Features.Music;

public sealed class MusicPlayerWindow : Window
{
    private readonly MusicPlayerService _service;
    private readonly ListBox _songs = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _heading = new() { FontSize = 11, Foreground = ViewFactory.MutedBrush, Margin = new(0, 2, 0, 5), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _current = new() { Text = "今天，听点什么？", FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = ViewFactory.Muted("免登录 · 三个音乐源 · 每日发现");
    private readonly TextBlock _status = ViewFactory.Status();
    private readonly TextBlock _time = ViewFactory.Muted("00:00 / 00:00");
    private readonly TextBlock _sleepStatus = ViewFactory.Muted("");
    private readonly Slider _progress = new() { Minimum = 0, Maximum = 1, IsMoveToPointEnabled = true };
    private readonly Button _play;
    private readonly Button _refresh;
    private readonly Button _folder;
    private readonly ComboBox _style;
    private readonly ComboBox _count;
    private readonly TextBlock _loadingText = ViewFactory.Muted("正在筛选可播放歌曲…");
    private readonly Border _loadingOverlay;
    private readonly RotateTransform _loadingRotation = new(0, 16, 16);
    private bool _spinnerRunning;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _updatingProgress;
    private bool _dragging;
    private int _batch;
    private IReadOnlyList<MusicTrack>? _shown;
    public MusicPlayerWindow() : this(AppController.Music) { }
    internal MusicPlayerWindow(MusicPlayerService service)
    {
        _service = service; Title = "音乐播放"; Width = 340; Height = 460;
        MaxHeight = SystemParameters.WorkArea.Height;
        ViewFactory.Shell(this);
        var root = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel();
        var intro = new StackPanel { Margin = new(2, 0, 2, 8) };
        intro.Children.Add(_current); _artist.Margin = new(0, 3, 0, 0);
        _artist.FontSize = 11; _artist.TextWrapping = TextWrapping.NoWrap; _artist.TextTrimming = TextTrimming.CharacterEllipsis;
        intro.Children.Add(_artist); top.Children.Add(intro);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 4) };
        var source = new ComboBox { Width = 90, FontSize = 11, ItemsSource = MusicCatalog.Sources, SelectedItem = MusicCatalog.Sources.First(s => s.Id == service.Settings.Source), ToolTip = "选择在线音源或本地音乐", Margin = new(0, 0, 4, 0) };
        var style = new ComboBox
        {
            Width = 94, FontSize = 11, Margin = new(0, 0, 4, 0), ToolTip = "选择音乐风格",
            ItemsSource = MusicCatalog.Styles.Select(s => new KeyValuePair<string, string>(s.Id,
                s.Id switch { "light" => "轻音乐", "folk" => "民谣", "dance" => "DJ 舞曲", _ => s.Name })).ToArray(),
            DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = service.Settings.Style
        };
        var count = new ComboBox { Width = 62, FontSize = 11, Margin = new(0, 0, 4, 0), ItemsSource = new[] { 10, 20, 30 }, SelectedItem = service.Settings.Count, ToolTip = "每日推荐首数" };
        _style = style; _count = count;
        var countText = new FrameworkElementFactory(typeof(TextBlock));
        countText.SetBinding(TextBlock.TextProperty, new Binding { StringFormat = "{0}首" });
        count.ItemTemplate = new DataTemplate { VisualTree = countText };
        filters.Children.Add(source); filters.Children.Add(style); filters.Children.Add(count);
        _folder = ViewFactory.Action("选择文件夹", async (_, _) => await ChooseFolderAsync(), true);
        _folder.Width = 160; _folder.Height = 28; _folder.FontSize = 11; _folder.Margin = new Thickness(0, 0, 4, 0);
        _folder.Padding = new Thickness(5, 3, 5, 3); filters.Children.Add(_folder);
        _refresh = ViewFactory.Action("换一批", async (_, _) => await service.LoadAsync(service.Settings.Source == "local" ? 0 : ++_batch), true);
        _refresh.Width = 58; _refresh.Height = 28; _refresh.FontSize = 11;
        _refresh.Padding = new Thickness(5, 3, 5, 3); _refresh.Margin = new Thickness(0);
        filters.Children.Add(_refresh);
        top.Children.Add(filters);
        top.Children.Add(_heading);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

        var bottom = new StackPanel { Margin = new(0, 5, 0, 0) };
        var timeline = new DockPanel();
        _time.FontSize = 10; _time.VerticalAlignment = VerticalAlignment.Center; _time.Margin = new(6, 0, 0, 0);
        DockPanel.SetDock(_time, Dock.Right); timeline.Children.Add(_time); timeline.Children.Add(_progress);
        bottom.Children.Add(timeline);
        var playback = new WrapPanel { Margin = new(0, 2, 0, 0) };
        playback.Children.Add(ViewFactory.Action("上一首", async (_, _) => await service.PreviousAsync(), true));
        _play = ViewFactory.Action("播放", async (_, _) =>
        {
            if (!service.IsPlaying && !service.IsOpening && _songs.SelectedIndex >= 0 && _songs.SelectedIndex != service.Index)
                await service.PlayAsync(_songs.SelectedIndex);
            else await service.ToggleAsync();
        }); _play.Width = 64; playback.Children.Add(_play);
        playback.Children.Add(ViewFactory.Action("下一首", async (_, _) => await service.NextAsync(), true));
        playback.Children.Add(ViewFactory.Action("停止", (_, _) => service.Stop(), true));
        foreach (var button in playback.Children.OfType<Button>())
        {
            button.FontSize = 11; button.Padding = new Thickness(6, 4, 6, 4);
            button.Margin = new Thickness(0, 0, 4, 2); button.Height = 28;
        }
        var mode = new ComboBox { Width = 90, FontSize = 11, ItemsSource = new[] { "顺序播放", "列表循环", "单曲循环", "随机播放" }, SelectedItem = service.Settings.Mode, ToolTip = "播放模式" };
        playback.Children.Add(mode); bottom.Children.Add(playback);
        var options = new WrapPanel { Margin = new(0, 2, 0, 0) };
        options.Children.Add(new TextBlock { Text = "音量", VerticalAlignment = VerticalAlignment.Center });
        var volume = new Slider { Width = 116, Minimum = 0, Maximum = 1, Value = service.Settings.Volume, SmallChange = .05, LargeChange = .1, ToolTip = "仅调整音乐音量；拉到最左侧静音", IsMoveToPointEnabled = true };
        options.Children.Add(volume);
        options.Children.Add(new TextBlock { Text = "定时", Margin = new(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        var sleep = new ComboBox { Width = 100, ItemsSource = new[] { "关闭", "15 分钟", "30 分钟", "60 分钟", "90 分钟" }, SelectedIndex = 0, ToolTip = "定时停止播放" };
        options.Children.Add(sleep); bottom.Children.Add(options);
        _sleepStatus.FontSize = 10; _sleepStatus.Height = 14; _sleepStatus.Margin = new(0, 2, 0, 0); bottom.Children.Add(_sleepStatus);
        _status.Height = 18; _status.FontSize = 11; _status.Margin = new(0, 2, 0, 0); _status.TextWrapping = TextWrapping.NoWrap; bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var template = new DataTemplate(typeof(MusicTrack));
        var itemStyle = new Style(typeof(ListBoxItem), (Style)FindResource(typeof(ListBoxItem)));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 2, 5, 2)));
        _songs.ItemContainerStyle = itemStyle;
        var row = new FrameworkElementFactory(typeof(TextBlock));
        row.SetBinding(TextBlock.TextProperty, new Binding(nameof(MusicTrack.Display)));
        row.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(MusicTrack.Display)));
        row.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        template.VisualTree = row; _songs.ItemTemplate = template;
        _songs.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var listArea = new Grid();
        listArea.Children.Add(_songs);
        var loadingContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ring = new Grid { Width = 32, Height = 32, Margin = new Thickness(0, 0, 0, 10) };
        ring.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Margin = new Thickness(3), Stroke = (Brush)FindResource("BorderBrushSoft"), StrokeThickness = 3
        });
        ring.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 16,3 A 13,13 0 1 1 3,16"), Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = _loadingRotation
        });
        loadingContent.Children.Add(ring);
        loadingContent.Children.Add(_loadingText);
        _loadingOverlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
            BorderBrush = (Brush)FindResource("BorderBrushSoft"), BorderThickness = new Thickness(1),
            Child = loadingContent, Visibility = Visibility.Collapsed
        };
        System.Windows.Automation.AutomationProperties.SetName(_loadingOverlay, "正在筛选可播放歌曲");
        listArea.Children.Add(_loadingOverlay);
        root.Children.Add(listArea); Content = ViewFactory.Frame(this, root, "音乐播放");

        source.SelectionChanged += async (_, _) =>
        {
            service.Settings.Source = ((MusicSource)source.SelectedItem).Id;
            UpdateSourceControls(); await ReloadAsync();
            if (service.Settings.Source == "local" && string.IsNullOrWhiteSpace(service.Settings.LocalFolder)) await ChooseFolderAsync();
        };
        style.SelectionChanged += async (_, _) => { service.Settings.Style = (string)style.SelectedValue; await ReloadAsync(); };
        count.SelectionChanged += async (_, _) => { service.Settings.Count = (int)count.SelectedItem; await ReloadAsync(); };
        mode.SelectionChanged += (_, _) => { service.Settings.Mode = (string)mode.SelectedItem; _ = AppController.SaveSettingsAsync(); };
        volume.ValueChanged += (_, _) => service.SetVolume(volume.Value);
        volume.LostMouseCapture += (_, _) => _ = AppController.SaveSettingsAsync();
        sleep.SelectionChanged += (_, _) => service.SetSleep(new[] { 0, 15, 30, 60, 90 }[sleep.SelectedIndex]);
        _songs.MouseDoubleClick += async (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_songs, e.OriginalSource as DependencyObject) is ListBoxItem && _songs.SelectedIndex >= 0)
                await service.PlayAsync(_songs.SelectedIndex);
        };
        _songs.KeyDown += async (_, e) => { if (e.Key == Key.Enter && _songs.SelectedIndex >= 0) { e.Handled = true; await service.PlayAsync(_songs.SelectedIndex); } };
        _progress.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _dragging = true));
        _progress.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { _dragging = false; service.Seek(_progress.Value); }));
        _progress.ValueChanged += (_, _) => { if (!_updatingProgress && !_dragging) service.Seek(_progress.Value); };
        _timer.Tick += (_, _) => UpdateProgress();
        service.Changed += ServiceChanged;
        IsVisibleChanged += (_, _) => UpdateLoading();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "China Standard Time"));
            if (!service.IsLoading && (service.Queue.Count == 0 || (service.Settings.Source != "local" && service.RecommendationDate != today && !service.IsPlaying && !service.IsOpening)))
                await service.LoadAsync();
        };
        Closed += (_, _) =>
        {
            _timer.Stop(); service.Changed -= ServiceChanged;
            _loadingRotation.BeginAnimation(RotateTransform.AngleProperty, null); _spinnerRunning = false;
            _ = AppController.SaveSettingsAsync();
        };
        Update();
    }
    private async Task ChooseFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择音乐文件夹（包含子文件夹）", Multiselect = false,
            InitialDirectory = System.IO.Directory.Exists(_service.Settings.LocalFolder) ? _service.Settings.LocalFolder : ""
        };
        if (dialog.ShowDialog(this) != true) return;
        _service.Settings.LocalFolder = dialog.FolderName;
        UpdateSourceControls(); await ReloadAsync();
    }
    private void UpdateSourceControls()
    {
        var local = _service.Settings.Source == "local";
        _style.Visibility = _count.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        _folder.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        _folder.Content = string.IsNullOrWhiteSpace(_service.Settings.LocalFolder) ? "选择文件夹" : "更换文件夹";
        _folder.ToolTip = (_service.Settings.LocalFolder.Length > 0 ? _service.Settings.LocalFolder + "\n" : "")
            + "包含子文件夹 · " + LocalMusicLibrary.FormatDescription;
        _refresh.Content = local ? "刷新" : "换一批";
        _loadingText.Text = local ? "正在读取音乐文件夹…" : "正在筛选可播放歌曲…";
        System.Windows.Automation.AutomationProperties.SetName(_loadingOverlay, _loadingText.Text);
    }
    private async Task ReloadAsync() { _batch = 0; _ = AppController.SaveSettingsAsync(); await _service.LoadAsync(); }
    private void ServiceChanged(object? sender, EventArgs e) => Update();
    private void Update()
    {
        UpdateSourceControls();
        if (!ReferenceEquals(_shown, _service.Queue)) { _shown = _service.Queue; _songs.ItemsSource = _shown; }
        _heading.Text = _service.RecommendationLabel;
        _heading.ToolTip = _heading.Text;
        _current.Text = _service.Current?.Title ?? "今天，听点什么？";
        _current.ToolTip = _current.Text;
        _artist.Text = _service.Current is { } track ? $"{track.Artist} · {MusicCatalog.Sources.First(s => s.Id == track.Source).Name}"
            : _service.Settings.Source == "local" ? "选择文件夹 · 离线播放 · 包含子文件夹" : "免登录 · 三个在线源 · 每日发现";
        _artist.ToolTip = _artist.Text;
        _status.Text = _service.Status;
        _status.ToolTip = string.IsNullOrEmpty(_service.LastFailure) ? _service.Status : $"{_service.Status}\n{_service.LastFailure}";
        _play.Content = _service.IsOpening ? "取消连接" : _service.IsPlaying ? "暂停" : "播放";
        _play.IsEnabled = _service.Queue.Count > 0;
        _refresh.IsEnabled = !_service.IsLoading;
        UpdateLoading();
        if (_service.Index >= 0) _songs.SelectedIndex = _service.Index;
        UpdateProgress();
    }
    private void UpdateLoading()
    {
        _loadingOverlay.Visibility = _service.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        _songs.IsEnabled = !_service.IsLoading;
        var animate = _service.IsLoading && IsVisible;
        if (_spinnerRunning == animate) return;
        _spinnerRunning = animate;
        _loadingRotation.BeginAnimation(RotateTransform.AngleProperty, animate
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(.9)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }
    private void UpdateProgress()
    {
        _updatingProgress = true;
        _progress.IsEnabled = _service.Duration > TimeSpan.Zero;
        _progress.Maximum = Math.Max(1, _service.Duration.TotalSeconds);
        if (!_dragging) _progress.Value = _service.Position.TotalSeconds;
        _updatingProgress = false;
        _time.Text = $"{Format(_service.Position)} / {Format(_service.Duration)}";
        _sleepStatus.Text = _service.StopAt is { } end ? $"将在 {end.LocalDateTime:HH:mm} 停止 · 剩余 {Math.Max(0, Math.Ceiling((end - DateTimeOffset.Now).TotalMinutes))} 分钟" : "";
    }
    private static string Format(TimeSpan span) => $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
}
