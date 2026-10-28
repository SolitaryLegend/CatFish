using System.Windows;
using CatFish.UI;
using CatFish.Features.Music;
using System.Globalization;
using Forms = System.Windows.Forms;

namespace CatFish.Core;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly TrayMenuRenderer _renderer = new();
    private readonly EventHandler _automationChanged;
    private readonly MusicPlayerService _music;
    private readonly EventHandler _musicChanged;
    private bool _disposed;

    public TrayService(MainWindow main) : this(main, AppController.Music) { }
    internal TrayService(MainWindow main, MusicPlayerService musicService)
    {
        _music = musicService;
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("显示主窗口", null, (_, _) => Dispatch(main.RestoreFromTray));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("窗口透明", null, (_, _) => Dispatch(() => AppController.OpenTool("transparency")));
        var automation = new Forms.ToolStripMenuItem("切换与滚动") { Name = "automation" };
        automation.DropDownItems.Add("程序设置", null, (_, _) => Dispatch(() => AppController.OpenTool("switcher")));
        var switching = new Forms.ToolStripMenuItem("窗口切换") { Name = "switch-tasks" };
        switching.DropDownItems.Add("选择程序", null, (_, _) => Dispatch(() => AppController.SelectAutomationPrograms(true)));
        var startSwitch = switching.DropDownItems.Add("开始", null, (_, _) => Dispatch(() =>
        {
            if (!AppController.StartSwitching()) ShowTaskMessage("窗口切换", AppController.Automation.SwitchStatus);
        }));
        var stopSwitch = switching.DropDownItems.Add("停止", null, (_, _) => Dispatch(AppController.Automation.StopSwitching));
        var scrolling = new Forms.ToolStripMenuItem("内容滚动") { Name = "scroll-tasks" };
        scrolling.DropDownItems.Add("选择程序", null, (_, _) => Dispatch(() => AppController.SelectAutomationPrograms(false)));
        var startScroll = scrolling.DropDownItems.Add("开始", null, (_, _) => Dispatch(() =>
        {
            if (!AppController.StartScrolling()) ShowTaskMessage("内容滚动", AppController.Automation.ScrollStatus);
        }));
        var stopScroll = scrolling.DropDownItems.Add("停止", null, (_, _) => Dispatch(AppController.Automation.StopScrolling));
        automation.DropDownItems.Add(switching); automation.DropDownItems.Add(scrolling);
        _menu.Items.Add(automation);
        var simulation = new Forms.ToolStripMenuItem("系统模拟");
        simulation.DropDownItems.Add("程序设置", null, (_, _) => Dispatch(() => AppController.OpenTool("simulation")));
        foreach (var (title, scene) in new[] { ("Windows 10 蓝屏", "Blue"), ("Windows 11 黑屏", "Black"), ("系统更新进度", "Update"), ("程序无响应提示", "Crash") })
            simulation.DropDownItems.Add(title, null, (_, _) => Dispatch(() => AppController.ShowSimulationScene(scene)));
        _menu.Items.Add(simulation);
        _menu.Items.Add("广告弹窗", null, (_, _) => Dispatch(() => AppController.OpenTool("popup")));
        var reader = new Forms.ToolStripMenuItem("单行阅读");
        reader.DropDownItems.Add("程序设置", null, (_, _) => Dispatch(() => AppController.OpenTool("txt-reader")));
        reader.DropDownItems.Add("显示 / 关闭浮窗", null, (_, _) => Dispatch(AppController.ToggleTxtReader));
        reader.DropDownItems.Add("播放 / 暂停", null, (_, _) => Dispatch(() => AppController.TxtReader.TogglePlayback()));
        reader.DropDownItems.Add("上一页", null, (_, _) => Dispatch(() => AppController.TxtReader.Previous()));
        reader.DropDownItems.Add("下一页", null, (_, _) => Dispatch(() => AppController.TxtReader.Next()));
        reader.DropDownItems.Add("恢复默认位置及可见样式", null, (_, _) => Dispatch(AppController.RestoreTxtReader));
        _menu.Items.Add(reader);
        var music = new Forms.ToolStripMenuItem("音乐播放");
        music.DropDownItems.Add("程序设置", null, (_, _) => Dispatch(() => AppController.OpenTool("music")));
        music.DropDownItems.Add("播放 / 暂停", null, (_, _) => Dispatch(AppController.ToggleMusicPlayback));
        music.DropDownItems.Add("上一首", null, (_, _) => Dispatch(() => { _ = AppController.Music.PreviousAsync(); }));
        music.DropDownItems.Add("下一首", null, (_, _) => Dispatch(() => { _ = AppController.Music.NextAsync(); }));
        music.DropDownItems.Add("停止", null, (_, _) => Dispatch(() => AppController.Music.Stop()));
        _menu.Items.Add(music);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        void UpdateTaskMenus()
        {
            startSwitch.Enabled = !AppController.Automation.IsSwitching; stopSwitch.Enabled = AppController.Automation.IsSwitching;
            startScroll.Enabled = !AppController.Automation.IsScrolling; stopScroll.Enabled = AppController.Automation.IsScrolling;
        }
        _automationChanged = (_, _) => Dispatch(UpdateTaskMenus);
        AppController.Automation.Changed += _automationChanged; UpdateTaskMenus();
        _menu.Items.Add("快捷键设置", null, (_, _) => Dispatch(() => AppController.ShowFeature("hotkeys", () => new HotkeySettingsWindow())));
        _menu.Items.Add("紧急停止与恢复", null, (_, _) => Dispatch(AppController.EmergencyStop));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出 CatFish", null, (_, _) => Dispatch(AppController.RequestExit));
        _renderer.Apply(_menu);
        using (var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/CatFish;component/Assets/CatFish.ico")).Stream)
            _trayIcon = new System.Drawing.Icon(iconStream, 32, 32);
        _icon = new Forms.NotifyIcon
        {
            Text = "CatFish · 双击打开主窗口",
            Icon = _trayIcon,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => Dispatch(main.RestoreFromTray);
        _musicChanged = (_, _) => Dispatch(UpdateMusicTooltip);
        _music.Changed += _musicChanged;
        UpdateMusicTooltip();
    }

    private void UpdateMusicTooltip()
    {
        if (!_disposed) _icon.Text = MusicTooltip(_music);
    }
    internal static string MusicTooltip(MusicPlayerService music)
    {
        if (music.Current is not { } track || (!music.IsPlaying && !music.IsPaused)) return "CatFish · 双击打开主窗口";
        var text = (music.IsPlaying ? "音乐播放中 · " : "音乐暂停中 · ") + track.Title;
        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Replace('\0', ' ');
        if (text.Length <= 63) return text;
        // NotifyIcon's native tooltip is bounded; never cut a surrogate pair or combining character.
        var elements = StringInfo.GetTextElementEnumerator(text);
        var result = "";
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (result.Length + element.Length > 62) break;
            result += element;
        }
        return result + "…";
    }

    private void ShowTaskMessage(string title, string message) => _icon.ShowBalloonTip(3000, title, message, Forms.ToolTipIcon.Info);

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) action(); else dispatcher.BeginInvoke(action);
    }
    public void Dispose() { _disposed = true; _music.Changed -= _musicChanged; AppController.Automation.Changed -= _automationChanged; _icon.Visible = false; _icon.Dispose(); _trayIcon.Dispose(); _menu.Dispose(); _renderer.Dispose(); }
}
