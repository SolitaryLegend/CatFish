using CatFish.Features.Reader;
using CatFish.Features.Music;

namespace CatFish.Core;

public sealed class AppSettings
{
    public MusicSettings Music { get; set; } = new();
    public TxtReaderSettings TxtReader { get; set; } = new();
    public List<WebsiteBookmark> WebsiteBookmarks { get; set; } = new();
    public bool MainWindowTopmost { get; set; }
    public int SwitchMinSeconds { get; set; } = 30;
    public int SwitchMaxSeconds { get; set; } = 90;
    public string SwitchMode { get; set; } = "Random";
    public bool PreferDifferentWindow { get; set; } = true;
    public int ScrollMinSeconds { get; set; } = 3;
    public int ScrollMaxSeconds { get; set; } = 7;
    public int ScrollRows { get; set; } = 3;
    public bool ScrollRandomRows { get; set; } = true;
    public bool ScrollRandomPause { get; set; } = true;
    public string ScrollDirection { get; set; } = "Down";
    public bool ScrollMayActivateTarget { get; set; }
    public bool SimulationRandomProgress { get; set; } = true;
    public bool SimulationAnimateProgress { get; set; } = true;
    public bool SimulationFullScreen { get; set; } = true;
    public string PopupCorner { get; set; } = "BottomRight";
    public int PopupMonitorIndex { get; set; }
    public bool PopupUseCustomPosition { get; set; }
    public double? PopupCustomLeft { get; set; }
    public double? PopupCustomTop { get; set; }
    public int PopupLayoutVersion { get; set; }
    public int PopupWidth { get; set; } = 480;
    public int PopupHeight { get; set; } = 633;
    public bool PopupAspectRatioLocked { get; set; } = true;
    public string PopupBackground { get; set; } = Features.Popup.PopupBackgrounds.DefaultId;
    public int PopupAutoHideSeconds { get; set; }
    public int PopupFrequencySeconds { get; set; } = 90;
    public bool PopupLocked { get; set; }
    public bool PopupCloseSelectedApplication { get; set; }
    public double PopupOpacity { get; set; } = 1;
    public int PopupGradientHeight { get; set; } = 62;
    public int PopupSideMaskWidth { get; set; } = 7;
    public double PopupVideoOpacity { get; set; } = .84;
    public int PopupBlurRadius { get; set; } = 3;
    public int HotkeyLayoutVersion { get; set; }
    public bool HotkeysDisabled { get; set; }
    public Dictionary<string, string> Hotkeys { get; set; } = new()
    {
        ["DecreaseOpacity"] = "Ctrl+Shift+J", ["IncreaseOpacity"] = "Ctrl+Shift+K",
        ["ResetOpacity"] = "Ctrl+Shift+L", ["TogglePopup"] = "Ctrl+Shift+T",
        ["EmergencyStop"] = "Ctrl+Shift+X",
        ["ToggleTxtReader"] = "Ctrl+Shift+Y", ["TxtPrevious"] = "Ctrl+Up",
        ["TxtNext"] = "Ctrl+Down", ["TxtPlayback"] = "Ctrl+Alt+F4",
        ["MusicPlayback"] = "Ctrl+Alt+F5", ["MusicPrevious"] = "Ctrl+Alt+Left",
        ["MusicNext"] = "Ctrl+Alt+Right"
    };
}
