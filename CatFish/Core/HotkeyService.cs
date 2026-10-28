using System.Windows.Input;
using System.Windows.Interop;
using System.Windows;

namespace CatFish.Core;

public sealed class HotkeyService : IDisposable
{
    private static readonly (string Action, int Id)[] Bindings =
    {
        ("DecreaseOpacity", 302), ("TogglePopup", 306), ("EmergencyStop", 307),
        ("IncreaseOpacity", 308), ("ResetOpacity", 309),
        ("ToggleTxtReader", 310), ("TxtPrevious", 311), ("TxtNext", 312), ("TxtPlayback", 313),
        ("MusicPlayback", 314), ("MusicPrevious", 315), ("MusicNext", 316)
    };
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = new();
    private bool _suspended;
    public event Action<string>? Pressed;

    public HotkeyService(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("无法初始化窗口快捷键。");
        _source.AddHook(WndProc);
    }

    public IReadOnlyList<string> RegisterDefaults()
    {
        var failed = new List<string>();
        if (_suspended) return failed;
        foreach (var (action, id) in Bindings)
        {
            var chord = AppController.Settings.Hotkeys.GetValueOrDefault(action);
            if (chord == "") continue;
            if (chord is null || !Register(id, chord)) failed.Add(Describe(action));
        }
        return failed;
    }

    public bool TrySet(string action, string shortcut, out string error)
    {
        if (_suspended) { error = "请先结束按键录入，再应用快捷键。"; return false; }
        var binding = Bindings.FirstOrDefault(x => x.Action == action);
        if (binding == default) { error = "未知快捷键操作。"; return false; }
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            if (action == "EmergencyStop") { error = "紧急停止快捷键必须保持可用。"; return false; }
            NativeMethods.UnregisterHotKey(_source.Handle, binding.Id); _registered.Remove(binding.Id); error = ""; return true;
        }
        if (!TryParse(shortcut, out var modifiers, out var key)) { error = "请按 Ctrl、Alt 或 Shift 加字母、功能键或方向键。"; return false; }
        foreach (var other in Bindings.Where(x => x.Action != action))
        {
            if (AppController.Settings.Hotkeys.TryGetValue(other.Action, out var chord) && TryParse(chord, out var otherModifiers, out var otherKey) && otherModifiers == modifiers && otherKey == key)
            { error = $"此组合已分配给“{Describe(other.Action)}”。"; return false; }
        }
        var oldChord = AppController.Settings.Hotkeys.GetValueOrDefault(action) ?? string.Empty;
        NativeMethods.UnregisterHotKey(_source.Handle, binding.Id); _registered.Remove(binding.Id);
        if (!NativeMethods.RegisterHotKey(_source.Handle, binding.Id, modifiers | NativeMethods.ModNoRepeat, key))
        {
            if (oldChord.Length > 0) Register(binding.Id, oldChord);
            error = "Windows 无法注册该快捷键。它可能已被其他程序占用。"; return false;
        }
        _registered.Add(binding.Id); error = string.Empty; return true;
    }

    private bool Register(int id, string chord)
    {
        if (!TryParse(chord, out var modifiers, out var key)) return false;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers | NativeMethods.ModNoRepeat, key)) return false;
        _registered.Add(id); return true;
    }

    public void Suspend()
    {
        _suspended = true;
        foreach (var id in _registered) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
    }

    public IReadOnlyList<string> Resume()
    {
        if (!_suspended) return Array.Empty<string>();
        _suspended = false;
        return RegisterDefaults();
    }

    internal static string? FromKey(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None || (modifiers & ModifierKeys.Windows) != 0
            || key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed) return null;
        var chord = ((modifiers & ModifierKeys.Control) != 0 ? "Ctrl+" : "")
            + ((modifiers & ModifierKeys.Alt) != 0 ? "Alt+" : "")
            + ((modifiers & ModifierKeys.Shift) != 0 ? "Shift+" : "")
            + new KeyConverter().ConvertToInvariantString(key);
        return TryParse(chord, out _, out _) ? chord : null;
    }

    internal static string DisplayChord(string chord) => chord.Replace("+Up", "+↑").Replace("+Down", "+↓")
        .Replace("+Left", "+←").Replace("+Right", "+→");

    private static bool TryParse(string chord, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0; virtualKey = 0;
        if (string.IsNullOrWhiteSpace(chord)) return false;
        var keyPart = string.Empty;
        foreach (var part in chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= NativeMethods.ModControl; break;
                case "alt": modifiers |= NativeMethods.ModAlt; break;
                case "shift": modifiers |= NativeMethods.ModShift; break;
                default: if (keyPart.Length > 0) return false; keyPart = part; break;
            }
        }
        if (modifiers == 0 || keyPart.Length == 0) return false;
        try
        {
            var key = (Key)new KeyConverter().ConvertFromInvariantString(keyPart)!;
            virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            return virtualKey != 0 && key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin);
        }
        catch { return false; }
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotkey) return nint.Zero;
        var id = wParam.ToInt32(); var binding = Bindings.FirstOrDefault(x => x.Id == id);
        if (!_suspended && binding != default) Pressed?.Invoke(binding.Action);
        handled = true; return nint.Zero;
    }

    private static string Describe(string action) => action switch
    {
        "DecreaseOpacity" => "降低透明度", "IncreaseOpacity" => "增加透明度", "ResetOpacity" => "恢复100%",
        "TogglePopup" => "显示/隐藏广告弹窗",
        "ToggleTxtReader" => "显示/隐藏单行阅读", "TxtPrevious" => "单行阅读上一页", "TxtNext" => "单行阅读下一页", "TxtPlayback" => "单行阅读播放/暂停",
        "MusicPlayback" => "音乐播放/暂停", "MusicPrevious" => "音乐上一首", "MusicNext" => "音乐下一首", _ => "紧急停止与恢复"
    };

    public void Dispose()
    {
        foreach (var id in _registered) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear(); _source.RemoveHook(WndProc);
    }
}
