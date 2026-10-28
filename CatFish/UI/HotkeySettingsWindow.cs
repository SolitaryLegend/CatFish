using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using CatFish.Core;

namespace CatFish.UI;

public sealed class HotkeySettingsWindow : Window
{
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = ViewFactory.Status();
    private TextBox? _captureBox;
    private static readonly (string Action, string Name)[] Items =
    {
        ("DecreaseOpacity", "降低透明度"), ("IncreaseOpacity", "增加透明度"), ("ResetOpacity", "恢复100%"),
        ("TogglePopup", "显示/隐藏广告弹窗"),
        ("ToggleTxtReader", "显示/隐藏单行阅读"), ("TxtPrevious", "单行阅读上一页"), ("TxtNext", "单行阅读下一页"), ("TxtPlayback", "单行阅读播放/暂停"),
        ("MusicPlayback", "音乐播放/暂停"), ("MusicPrevious", "音乐上一首"), ("MusicNext", "音乐下一首"),
        ("EmergencyStop", "紧急停止与恢复")
    };

    public HotkeySettingsWindow()
    {
        Title = "快捷键设置 · CatFish"; Width = 540; Height = 620; MinWidth = 520; MinHeight = 390; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = ViewFactory.Shell(this);
        foreach (var (action, name) in Items) _rows.Children.Add(BuildRow(action, name));
        root.Children.Add(ViewFactory.Card(_rows));
        root.Children.Add(ViewFactory.Muted("点击输入框后直接按组合键，再点“应用”。Delete / Backspace 清空，Esc 取消录入；紧急停止不可停用。"));
        root.Children.Add(ViewFactory.Action("恢复推荐快捷键", (_, _) => ResetDefaults(), true)); root.Children.Add(_status);
        Content = ViewFactory.Frame(this, new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root }, "快捷键设置");
        Deactivated += (_, _) => EndCapture();
        Closed += (_, _) => EndCapture();
        Activated += (_, _) =>
        {
            if (_captureBox is { IsKeyboardFocusWithin: true }) AppController.BeginHotkeyCapture();
        };
    }

    private UIElement BuildRow(string action, string name)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }; grid.Children.Add(label);
        var chord = AppController.Settings.Hotkeys.GetValueOrDefault(action) ?? "";
        var box = new TextBox
        {
            Name = action + "Input", Text = HotkeyService.DisplayChord(chord), Tag = chord, IsReadOnly = true,
            IsReadOnlyCaretVisible = false, ContextMenu = null, VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0), ToolTip = "点击后直接按组合键"
        };
        InputMethod.SetIsInputMethodEnabled(box, false);
        System.Windows.Automation.AutomationProperties.SetName(box, name + "快捷键");
        box.GotKeyboardFocus += (_, _) =>
        {
            _captureBox = box; AppController.BeginHotkeyCapture(); box.SelectAll();
            _status.Text = $"正在录入“{name}”，请按组合键。";
        };
        box.LostKeyboardFocus += (_, _) => { EndCapture(); _captureBox = null; };
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;
            if (key == Key.Tab && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift)) return;
            e.Handled = true;
            if (e.IsRepeat) return;
            if (key == Key.Escape && modifiers == ModifierKeys.None)
            {
                SetChord(box, AppController.Settings.Hotkeys.GetValueOrDefault(action) ?? "");
                Keyboard.ClearFocus(); _status.Text = "已取消本次录入。"; return;
            }
            if ((key is Key.Back or Key.Delete) && modifiers == ModifierKeys.None)
            {
                if (action == "EmergencyStop") _status.Text = "紧急停止快捷键必须保持可用。";
                else { SetChord(box, ""); _status.Text = "已清空，点击“应用”停用该快捷键。"; }
                return;
            }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var captured = HotkeyService.FromKey(key, modifiers);
            if (captured is null) { _status.Text = "请使用 Ctrl、Alt 或 Shift 加字母、功能键或方向键。"; return; }
            SetChord(box, captured); _status.Text = $"已录入 {box.Text}，点击“应用”保存。";
        };
        Grid.SetColumn(box, 1); grid.Children.Add(box);
        var apply = ViewFactory.Action("应用", (_, _) =>
        {
            EndCapture(); Keyboard.ClearFocus();
            if (AppController.TrySetHotkey(action, (string)box.Tag, out var error)) _status.Text = box.Text.Length == 0 ? $"已停用“{name}”。" : $"已更新“{name}”：{box.Text}。";
            else _status.Text = error;
        }); Grid.SetColumn(apply, 2); grid.Children.Add(apply);
        return grid;
    }

    private void ResetDefaults()
    {
        EndCapture(); Keyboard.ClearFocus();
        var conflicts = AppController.ResetHotkeys();
        _rows.Children.Clear(); foreach (var (action, name) in Items) _rows.Children.Add(BuildRow(action, name));
        _status.Text = conflicts.Count == 0 ? "已恢复所有推荐快捷键。" : "以下快捷键注册冲突：" + string.Join("、", conflicts);
    }

    private static void SetChord(TextBox box, string chord)
    {
        box.Tag = chord; box.Text = HotkeyService.DisplayChord(chord); box.SelectAll();
    }

    private void EndCapture()
    {
        var conflicts = AppController.EndHotkeyCapture();
        if (conflicts.Count > 0) _status.Text = "以下快捷键注册冲突：" + string.Join("、", conflicts);
    }
}
