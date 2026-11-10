using System.IO;
using System.Text.Json;

namespace CatFish.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatFish", "settings.json");
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private static AppSettings CreateDefaultSettings() => new()
    {
        PopupLayoutVersion = Features.Popup.PopupLayout.Version, HotkeyLayoutVersion = 1, WebsiteBookmarksInitialized = true
    };

    internal static void MigrateLegacySettings(string? localAppData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // Keep the original configuration as a backup; an existing CatFish configuration wins.
        var legacyPath = Path.Combine(localAppData, "FishDesk", "settings.json");
        var currentPath = Path.Combine(localAppData, "CatFish", "settings.json");
        if (File.Exists(currentPath) || !File.Exists(legacyPath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
            File.Copy(legacyPath, currentPath, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return CreateDefaultSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? CreateDefaultSettings();
            settings.TxtReader ??= new(); settings.TxtReader.Normalize();
            settings.Music ??= new(); settings.Music.Normalize();
            settings.WebsiteBookmarks = (settings.WebsiteBookmarks ?? new()).Where(bookmark => bookmark is not null && !bookmark.IsEmpty).ToList();
            if (!settings.WebsiteBookmarksInitialized && settings.WebsiteBookmarks.Count == 0)
                settings.WebsiteBookmarks = WebsiteBookmark.CreateDefaults();
            settings.WebsiteBookmarksInitialized = true;
            NormalizeHotkeys(settings);
            if (settings.PopupLayoutVersion < 1)
            {
                if (settings.PopupWidth == 380 && settings.PopupHeight == 240) { settings.PopupWidth = 400; settings.PopupHeight = 530; }
                settings.PopupAutoHideSeconds = 0; settings.PopupOpacity = 1; settings.PopupLayoutVersion = 1;
            }
            if (settings.PopupLayoutVersion < 2)
            {
                if (settings.PopupWidth == 400 && settings.PopupHeight == 530) settings.PopupHeight = 500;
                if (settings.PopupGradientHeight == 56) settings.PopupGradientHeight = 72;
                settings.PopupLayoutVersion = 2;
            }
            if (settings.PopupLayoutVersion < 3)
            {
                if (settings.PopupWidth == 400 && settings.PopupHeight == 500) { settings.PopupWidth = 640; settings.PopupHeight = 400; }
                else if (settings.PopupWidth < settings.PopupHeight) (settings.PopupWidth, settings.PopupHeight) = (settings.PopupHeight, settings.PopupWidth);
                if (settings.PopupGradientHeight == 72) settings.PopupGradientHeight = 28;
                settings.PopupLayoutVersion = 3;
            }
            if (settings.PopupLayoutVersion < 4)
            {
                if (settings.PopupWidth == 640 && settings.PopupHeight == 400) settings.PopupHeight = 510;
                settings.PopupLayoutVersion = 4;
            }
            if (settings.PopupLayoutVersion < 5)
            {
                if (settings.PopupWidth == 640 && settings.PopupHeight == 510) settings.PopupHeight = 670;
                settings.PopupLayoutVersion = 5;
            }
            if (settings.PopupLayoutVersion < 6)
            {
                if (settings.PopupAspectRatioLocked)
                    (settings.PopupWidth, settings.PopupHeight) = Features.Popup.PopupLayout.SquareSize(settings.PopupWidth);
                settings.PopupLayoutVersion = 6;
            }
            if (!Features.Popup.PopupBackgrounds.Items.Any(background => background.Id == settings.PopupBackground)) settings.PopupBackground = Features.Popup.PopupBackgrounds.DefaultId;
            if (settings.PopupLayoutVersion < Features.Popup.PopupLayout.Version)
            {
                if (settings.PopupAspectRatioLocked)
                    (settings.PopupWidth, settings.PopupHeight) = Features.Popup.PopupLayout.BackgroundSize(settings.PopupWidth, settings.PopupBackground);
                settings.PopupLayoutVersion = Features.Popup.PopupLayout.Version;
            }
            settings.SwitchMinSeconds = Math.Clamp(settings.SwitchMinSeconds, 10, 180); settings.SwitchMaxSeconds = Math.Clamp(settings.SwitchMaxSeconds, settings.SwitchMinSeconds, 180);
            settings.ScrollMinSeconds = Math.Clamp(settings.ScrollMinSeconds, 1, 600); settings.ScrollMaxSeconds = Math.Clamp(settings.ScrollMaxSeconds, settings.ScrollMinSeconds, 600);
            settings.ScrollRows = Math.Clamp(settings.ScrollRows, 1, 10); settings.PopupWidth = Math.Clamp(settings.PopupWidth, Features.Popup.PopupLayout.MinWidth, 900); settings.PopupHeight = Math.Clamp(settings.PopupHeight, Features.Popup.PopupLayout.MinHeight, 700);
            settings.PopupAutoHideSeconds = Math.Clamp(settings.PopupAutoHideSeconds, 0, 600); settings.PopupFrequencySeconds = Math.Clamp(settings.PopupFrequencySeconds, 10, 3600);
            settings.PopupOpacity = Math.Clamp(settings.PopupOpacity, .5, 1); settings.PopupGradientHeight = Math.Clamp(settings.PopupGradientHeight, 0, 120); settings.PopupBlurRadius = Math.Clamp(settings.PopupBlurRadius, 0, 18);
            settings.PopupSideMaskWidth = Math.Clamp(settings.PopupSideMaskWidth, 0, 120);
            settings.PopupVideoOpacity = double.IsFinite(settings.PopupVideoOpacity) ? Math.Clamp(settings.PopupVideoOpacity, 0, 1) : 1;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            try { if (File.Exists(_path)) File.Copy(_path, _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", false); } catch { }
            return CreateDefaultSettings();
        }
    }
    internal static void NormalizeHotkeys(AppSettings settings)
    {
        var defaults = new AppSettings().Hotkeys;
        settings.Hotkeys ??= new();
        foreach (var removed in new[] { "OpenTransparency", "ToggleMain", "OpenSwitcher", "NextWindow", "ToggleReading" })
            settings.Hotkeys.Remove(removed);
        if (settings.HotkeyLayoutVersion < 1)
        {
            var legacy = new Dictionary<string, string>
            {
                ["TogglePopup"] = "Ctrl+Alt+Shift+P", ["EmergencyStop"] = "Ctrl+Alt+Shift+X",
                ["ToggleTxtReader"] = "Ctrl+Alt+Shift+B", ["TxtPrevious"] = "Ctrl+Alt+Shift+PageUp",
                ["TxtNext"] = "Ctrl+Alt+Shift+PageDown", ["TxtPlayback"] = "Ctrl+Alt+Shift+Space"
            };
            // Upgrade previous defaults without replacing deliberately customized or disabled bindings.
            foreach (var (action, chord) in legacy)
                if (settings.Hotkeys.TryGetValue(action, out var current) && string.Equals(current, chord, StringComparison.OrdinalIgnoreCase))
                    settings.Hotkeys[action] = defaults[action];
            settings.HotkeyLayoutVersion = 1;
        }
        foreach (var (action, chord) in defaults)
            if (!settings.Hotkeys.TryGetValue(action, out var current) || current is null)
                settings.Hotkeys[action] = chord;
        if (string.IsNullOrWhiteSpace(settings.Hotkeys["EmergencyStop"])) settings.Hotkeys["EmergencyStop"] = defaults["EmergencyStop"];
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        // Snapshot on the UI thread before waiting for an earlier asynchronous write.
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path)!; Directory.CreateDirectory(directory); var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
            File.Move(temp, _path, true);
        }
        finally { _saveLock.Release(); }
    }
}
