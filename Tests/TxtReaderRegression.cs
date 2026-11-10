using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CatFish.Core;
using CatFish.Features.Reader;
using CatFish.Features.Transparency;

internal static class TxtReaderRegression
{
    public static void Run(Action<bool, string> check, Action<Window, string> render)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "txt-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var saves = 0;
        var settings = new TxtReaderSettings { PreferNaturalBreaks = false };
        using var reader = new TxtReaderService(settings, () => { saves++; return Task.CompletedTask; });
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            const string sample = "第一段中文，Hello world!\r\n\r\n第二段  有空格；a\u0301 👨‍👩‍👧‍👦 😀。最后一段，阅读结束。";
            var original = string.Concat(Enumerable.Repeat(sample, 12));
            var first = Path.Combine(directory, "mixed.txt"); var second = Path.Combine(directory, "second.txt");
            File.WriteAllText(first, original, new UTF8Encoding(false));
            File.WriteAllText(second, "另一个文件，分别保存阅读进度。" + original);
            check(Wait(reader.OpenAsync(first)), "TXT opens UTF-8 without BOM and starts paused");
            check(!reader.IsPlaying && reader.Offset == 0 && reader.Document!.Text == original, "load keeps original text and does not autoplay");
            reader.SetViewport(30, text => new StringInfo(text).LengthInTextElements); // width is clamped to 80; use 90 below.
            foreach (var capacity in new[] { 16, 32, 64 })
            {
                var doc = reader.Document!; var at = 0; var visual = new StringBuilder(); var pages = 0; var valid = true;
                while (at < doc.Text.Length)
                {
                    var page = doc.Page(at, capacity, 26, text => new StringInfo(text).LengthInTextElements, true, true);
                    valid &= page.Start == at && page.End > at && page.ElementCount <= capacity && new StringInfo(page.Text).LengthInTextElements <= 26
                        && !page.Text.Contains('\n') && !page.Text.Contains('\r') && doc.SnapOffset(page.End) == page.End;
                    visual.Append(page.Text); at = page.End;
                    if (++pages > 10000) throw new InvalidOperationException("Pagination did not advance.");
                }
                check(valid && visual.ToString() == Regex.Replace(original, @"\s+", " "), $"{capacity}-element pagination preserves all normalized text, Unicode clusters and pixel limits");
            }
            check(reader.Document!.SnapOffset(original.IndexOf("👨", StringComparison.Ordinal) + 3) == original.IndexOf("👨", StringComparison.Ordinal),
                "seeking inside a ZWJ emoji snaps to the original grapheme boundary");
            var unc = reader.Document.Page(0, 120, 1000, text => new StringInfo(text).LengthInTextElements, false, false);
            check(unc.Text.Contains("  ") && !unc.Text.Contains('\n'), "uncompressed whitespace is kept on a single visual line");

            foreach (var (name, codec) in new (string, Encoding)[]
            {
                ("utf8-bom", new UTF8Encoding(true)), ("utf16-le", new UnicodeEncoding(false, true)),
                ("utf16-be", new UnicodeEncoding(true, true)), ("gb18030", Encoding.GetEncoding("gb18030")), ("gbk", Encoding.GetEncoding("gbk"))
            })
            {
                var path = Path.Combine(directory, name + ".txt"); const string chinese = "中文阅读测试，Hello 123。";
                File.WriteAllText(path, chinese, codec);
                var document = Wait(TxtDocument.LoadAsync(path, name == "gbk" ? "gbk" : "Auto"));
                check(document.Text == chinese, "TXT encoding decodes " + name);
            }
            var noBom = Path.Combine(directory, "utf16-no-bom.txt"); File.WriteAllText(noBom, "Hello 中文阅读 123", new UnicodeEncoding(false, false));
            check(Wait(TxtDocument.LoadAsync(noBom)).Text == "Hello 中文阅读 123", "BOM-less UTF-16 with Latin text is detected");
            var gbPath = Path.Combine(directory, "gb18030.txt");
            var keepDocument = reader.Document; var keepOffset = reader.Offset;
            check(!Wait(reader.OpenAsync(gbPath, "utf-8")) && ReferenceEquals(keepDocument, reader.Document) && reader.Offset == keepOffset,
                "wrong manual encoding reports failure and preserves current content");
            var empty = Path.Combine(directory, "empty.txt"); File.WriteAllText(empty, " \r\n ");
            check(!Wait(reader.OpenAsync(empty)) && reader.Status.Contains("为空") && ReferenceEquals(keepDocument, reader.Document), "empty TXT has a clear error and preserves the current document");
            check(!Wait(reader.OpenAsync(Path.Combine(directory, "missing.txt"))) && ReferenceEquals(keepDocument, reader.Document), "missing TXT does not replace the current document");
            var large = Path.Combine(directory, "too-large.txt"); using (var stream = File.Create(large)) stream.SetLength(TxtDocument.MaximumBytes + 1);
            check(!Wait(reader.OpenAsync(large)) && reader.Status.Contains("32 MB"), "resource limit rejects oversized TXT before indexing");

            settings.MaxCharacters = 16; reader.SetViewport(400, text => new StringInfo(text).LengthInTextElements); reader.RefreshLayout();
            reader.Seek(0); var firstEnd = reader.CurrentPage!.End; reader.Next(); var secondEnd = reader.CurrentPage!.End;
            check(reader.Offset == firstEnd, "manual next page uses the exact displayed source boundary");
            reader.Previous(); check(reader.Offset == 0, "previous returns to the exact prior displayed page");
            reader.SeekPercent(50); var savedOffset = reader.Offset;
            check(Wait(reader.OpenAsync(second)) && reader.Offset == 0, "new file starts with its independent offset");
            reader.Next(); var secondOffset = reader.Offset;
            check(Wait(reader.OpenAsync(first)) && reader.Offset == savedOffset && !reader.IsPlaying, "switching back restores first file progress paused");
            check(Wait(reader.OpenAsync(second)) && reader.Offset == secondOffset, "second file retains its own reading cursor");
            check(File.ReadAllText(first) == original, "reader never modifies source TXT contents");
            CheckTextLocation(directory, check);
            CheckRenderingClock(reader, check);
            settings.MaxCharacters = 16; reader.SetViewport(400, text => new StringInfo(text).LengthInTextElements); reader.RefreshLayout();

            reader.Seek(0); settings.SecondsPerPage = 1; settings.Mode = "Static"; reader.RefreshLayout(); reader.Play();
            reader.Advance(.5); check(reader.Offset == 0 && reader.ScrollOffset == 0, "static mode holds a page without animation");
            reader.Advance(.5); check(reader.Offset > 0 && reader.ScrollOffset == 0, "static mode replaces a page immediately at the configured interval");
            reader.Pause(); var paused = reader.Offset; reader.Advance(.8);
            check(reader.Offset == paused && !reader.IsPlaying, "paused static reading never advances");
            reader.Play(); reader.Advance(120); check(reader.Offset == paused, "resume or a blocked UI cannot skip pages in a burst");
            var beforeMode = reader.Offset; settings.Mode = "Scroll"; reader.RefreshLayout();
            check(reader.Offset == beforeMode, "mode change preserves the shared original-text cursor");
            reader.Advance(.1); var scrolled = reader.ScrollOffset;
            reader.Pause(); reader.Advance(.2); check(scrolled > 0 && reader.ScrollOffset == scrolled, "smooth playback pauses at its precise visual offset");
            reader.Play(); reader.Advance(.1); check(reader.ScrollOffset > scrolled, "smooth playback resumes from the paused position");
            var beforeSpeedChange = reader.ScrollOffset; settings.PixelsPerSecond = 80; reader.RefreshLayout(false); reader.Advance(.1);
            check(Math.Abs(reader.ScrollOffset - beforeSpeedChange - 8) < .001, "changing scroll speed applies on the next frame without restarting the segment");
            var beforeOpacityChange = reader.ScrollOffset; settings.TextOpacity = .5; reader.RefreshLayout(false);
            check(reader.ScrollOffset == beforeOpacityChange && reader.IsPlaying, "opacity preview does not restart or interrupt playback"); settings.TextOpacity = 1;
            settings.PauseOnManualNavigation = true; reader.Next(); check(!reader.IsPlaying, "manual navigation can pause automatic playback");
            settings.PauseOnManualNavigation = false;
            reader.Seek(0); reader.Play(); reader.Next(); check(reader.IsPlaying, "manual navigation continues playback by default");
            reader.Pause();
            settings.Mode = "Static"; settings.SecondsPerPage = 1; reader.Seek(reader.Document!.Text.Length - 1); reader.RefreshLayout(); reader.Play(); reader.Advance(1);
            check(!reader.IsPlaying && reader.CurrentPage!.Text.Length > 0 && reader.Status.Contains("阅读完成"), "EOF stops and retains the last segment");
            settings.EndBehavior = "Loop"; reader.Play(); reader.Advance(1); check(reader.Offset == 0 && reader.IsPlaying, "optional looping returns to the beginning");
            var hide = false; reader.HideRequested += (_, _) => hide = true;
            settings.EndBehavior = "Hide"; reader.Seek(reader.Document.Text.Length - 1); reader.Play(); reader.Advance(1);
            check(hide && !reader.IsPlaying, "optional EOF hiding stops playback and requests closing the reader"); settings.EndBehavior = "Stop";
            reader.Seek(0); settings.SecondsPerPage = 1; var beforeSaves = saves; reader.Play();
            for (var i = 0; i < 10; i++) reader.Advance(.5);
            check(saves > beforeSaves && saves - beforeSaves <= 2, "autoplay persists progress on a throttled cadence across short pages"); reader.Pause();

            var settingsFile = Path.Combine(directory, "settings.json"); var store = new SettingsStore();
            typeof(SettingsStore).GetField("_path", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(store, settingsFile);
            var appSettings = new AppSettings { TxtReader = settings };
            Wait(store.SaveAsync(appSettings)); var restoredSettings = store.Load().TxtReader;
            using (var restarted = new TxtReaderService(restoredSettings, () => Task.CompletedTask))
            {
                check(Wait(restarted.OpenAsync(settings.LastFile!)) && restarted.Offset == reader.Offset && !restarted.IsPlaying,
                    "restart restores the original offset from atomic JSON configuration without autoplay");
            }
            File.WriteAllText(settingsFile, "{broken");
            check(store.Load().TxtReader.MaxCharacters == 32 && Directory.GetFiles(directory, "settings.json.corrupt-*.bak").Length == 1,
                "corrupt settings are backed up and recover safe reader defaults");
            var legacyCapacity = new TxtReaderSettings { MaxCharacters = 120,
                RecentFiles = [new TxtReadingRecord { Path = first, Offset = savedOffset, FileSize = new FileInfo(first).Length, LastWriteUtc = File.GetLastWriteTimeUtc(first) }] };
            Wait(store.SaveAsync(new AppSettings { TxtReader = legacyCapacity }));
            var migratedCapacity = store.Load().TxtReader;
            check(migratedCapacity.MaxCharacters == 64 && migratedCapacity.RecentFiles.Single().Offset == savedOffset,
                "legacy capacity above 64 is limited on load without changing the original reading bookmark");
            var invalid = new TxtReaderSettings { MaxCharacters = -1, FontSize = double.NaN, Width = double.PositiveInfinity, RecentFiles = null! }; invalid.Normalize();
            check(invalid.MaxCharacters == 8 && invalid.FontSize == 22 && invalid.Width == 760 && invalid.RecentFiles.Count == 0, "reader settings sanitize invalid persisted values");

            reader.Pause(); reader.SeekPercent(50); File.WriteAllText(second, "更新后的短文本。" + sample);
            File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddSeconds(3));
            var beforeCancel = reader.Document;
            check(!Wait(reader.OpenAsync(second, "Auto", _ => null)) && ReferenceEquals(beforeCancel, reader.Document), "changed-file cancellation leaves the loaded document intact");
            check(Wait(reader.OpenAsync(second, "Auto", r => r.Offset)) && reader.Offset < reader.Document!.Text.Length && reader.Status.Contains("文本已更新"), "changed-file offset is safely clamped to the shorter text");
            File.WriteAllText(second, "再次更新。" + sample); File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddSeconds(5));
            check(Wait(reader.OpenAsync(second, "Auto", _ => 0)) && reader.Offset == 0, "changed text can explicitly restart from the beginning");

            using (var floating = new WindowScope(new TxtReaderWindow(reader)))
            {
                var window = (TxtReaderWindow)floating.Window; window.Show(); Pump(40);
                check(window.WindowStyle == WindowStyle.None && window.AllowsTransparency && !window.ShowInTaskbar && !window.ShowActivated && window.Topmost,
                    "reader is a compact transparent non-activating topmost window");
                check(settings.BackgroundOpacity == 0 && settings.TextOpacity == 1, "reader defaults keep background and text opacity independent");
                var handle = new WindowInteropHelper(window).Handle;
                NativeMethods.GetWindowRect(handle, out var initialRect);
                var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
                var dpi = VisualTreeHelper.GetDpi(window);
                check(Math.Abs((initialRect.Left + initialRect.Right) / 2d - (area.Left + area.Right) / 2d) <= 3
                    && Math.Abs(area.Bottom - initialRect.Bottom - 50 * dpi.DpiScaleY) <= 3,
                    "first show actually places text at the primary work-area bottom, after WPF startup layout");
                check(window.Title == "单行阅读 · CatFish", "floating reader uses the requested short name");
                CheckMouseInteraction(window, reader, check);
                foreach (var capacity in new[] { 16, 32, 64 })
                {
                    settings.Width = 220; settings.FontSize = 48; settings.MaxCharacters = capacity;
                    reader.RefreshLayout(); window.ApplySettings();
                    check(reader.CurrentPage!.ElementCount <= capacity && reader.MeasureText(reader.CurrentPage.Text) <= reader.ViewportWidth + .01,
                        $"real WPF metrics keep {capacity}-character text inside a narrow large-font window");
                }
                settings.Width = 760; settings.FontSize = 22; settings.MaxCharacters = 32; reader.RefreshLayout(); window.ApplySettings();
                check(!new WindowAppearanceService().TrySetOpacity(window, .1, out _), "generic window opacity cannot hide reader text accidentally");
                settings.TextOpacity = .4; settings.BackgroundOpacity = .2; reader.RefreshLayout();
                render(window, "txt-reader-translucent.png");
                settings.TextOpacity = 0; settings.BackgroundOpacity = 0; window.ResetVisibleStyle();
                check(settings.TextOpacity == 1 && settings.BackgroundOpacity == 0 && !settings.Locked, "invisible text can be recovered to the default style and position");
                window.Left = -10000; window.Top = -10000; window.SetDefaultPosition();
                check(window.Left >= 0 && window.Top >= 0 && settings.MonitorName is not null && settings.RelativeLeft is >= 0 and <= 1,
                    "reset brings an off-screen reader back and saves monitor-relative placement");
                render(window, "txt-reader-default.png");
                window.Left += 45; window.Top -= 18; window.SavePosition();
                NativeMethods.GetWindowRect(handle, out var savedRect);
                reader.Play(); window.Close(); check(!reader.IsPlaying, "closing the floating reader immediately stops its timer and saves progress");
                check(!window.HoverToolbar.IsOpen, "closing the reader also closes its hover toolbar");
                using var reopened = new WindowScope(new TxtReaderWindow(reader)); reopened.Window.Show(); Pump(60);
                NativeMethods.GetWindowRect(new WindowInteropHelper(reopened.Window).Handle, out var restoredRect);
                check(Math.Abs(restoredRect.Left - savedRect.Left) <= 3 && Math.Abs(restoredRect.Top - savedRect.Top) <= 3,
                    "first-show placement does not overwrite a previously saved user position");
            }

            var controller = AppController.TxtReader;
            Wait(controller.OpenAsync(first)); controller.SeekPercent(25);
            AppController.ShowTxtReader(); controller.Play(); AppController.EmergencyStop();
            check(!controller.IsPlaying && controller.Offset > 0, "global emergency stop pauses TXT reading and preserves progress");
            check(AppController.ReaderWindow is null, "global emergency stop closes the floating reader window");
            AppController.ReaderWindow?.Close();
            var actualHistory = controller.Settings.RecentFiles.ToArray();
            foreach (var historyPath in new[] { second, gbPath, noBom })
                controller.Settings.RecentFiles.Add(new TxtReadingRecord { Path = historyPath });
            var settingsWindow = new TxtReaderSettingsWindow();
            render(settingsWindow, "txt-reader-settings.png");
            var tabs = Descendants<TabControl>((DependencyObject)settingsWindow.Content).Single();
            check(settingsWindow.Title == "单行阅读 · CatFish" && settingsWindow.Width < 480 && settingsWindow.Height < 640,
                "reader settings use the short name and a smaller compact window");
            var readingButtons = Descendants<Button>((DependencyObject)settingsWindow.Content).Select(button => button.Content as string).ToArray();
            check(readingButtons.Contains("开始") && readingButtons.Contains("停止") && !readingButtons.Contains("暂停"),
                "reading tab exposes start and stop without a pause button");
            check(!Descendants<TextBlock>((DependencyObject)settingsWindow.Content).Any(text => text.Text.Contains("编码")),
                "reading tab omits encoding controls and preview");
            CheckTextLocator(settingsWindow, controller, check);
            for (var i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i; render(settingsWindow, $"txt-reader-settings-tab{i}.png");
                var page = (ScrollViewer)((TabItem)tabs.Items[i]).Content;
                check(page.VerticalScrollBarVisibility == ScrollBarVisibility.Hidden && page.ScrollableHeight < .5,
                    $"reader tab {i + 1} fits completely without a page scrollbar");
                var groups = Descendants<GroupBox>((DependencyObject)settingsWindow.Content).ToArray();
                check(groups.Length >= 3 && groups.All(group => group.Template is not null && group.BorderThickness.Left == 1 && group.Header is string),
                    $"reader tab {i + 1} groups its content in thin frames with corner captions");
            }
            tabs.SelectedIndex = 0; var previewMode = controller.Settings.Mode;
            controller.Settings.Mode = "Scroll"; controller.RefreshLayout(); render(settingsWindow, "txt-reader-settings-scroll.png");
            controller.Settings.Mode = previewMode; controller.RefreshLayout();
            tabs.SelectedIndex = 1; settingsWindow.Show(); Pump(60);
            CheckColorPalette(settingsWindow, controller, check);
            CheckFontStepper(settingsWindow, controller, check);
            CheckSettingsWheel(settingsWindow, tabs, controller, check);
            var fonts = Descendants<ComboBox>((DependencyObject)settingsWindow.Content).Single(box => box.Name == "ReaderFontFamilyInput");
            var originalFont = controller.Settings.FontFamily; var originalOffset = controller.Offset;
            fonts.SelectedItem = fonts.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == "SimSun");
            check(fonts.Items.Count == 6 && controller.Settings.FontFamily == "SimSun" && controller.Offset == originalOffset,
                "font selection offers six common fonts and applies the selected family without changing the bookmark");
            fonts.IsDropDownOpen = true; Pump(40);
            var fontPopup = (System.Windows.Controls.Primitives.Popup)fonts.Template.FindName("PART_Popup", fonts);
            RenderElement((FrameworkElement)fontPopup.Child, "single-line-common-fonts.png");
            fonts.IsDropDownOpen = false; controller.Settings.FontFamily = originalFont; controller.RefreshLayout();
            check(tabs.Items.Count == 2 && (string)((TabItem)tabs.Items[1]).Header == "文字与浮窗", "reader settings combine appearance and floating-window options in the second tab"); settingsWindow.Close();
            controller.Settings.RecentFiles = actualHistory.ToList();

            var hotkeyWindow = new Window(); new WindowInteropHelper(hotkeyWindow).EnsureHandle();
            using (var hotkeys = new HotkeyService(hotkeyWindow))
            {
                var actions = new List<string>(); hotkeys.Pressed += actions.Add;
                var wndProc = typeof(HotkeyService).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance)!;
                foreach (var id in new[] { 310, 311, 312, 313 }) wndProc.Invoke(hotkeys, new object[] { nint.Zero, NativeMethods.WmHotkey, new nint(id), nint.Zero, false });
                check(actions.SequenceEqual(new[] { "ToggleTxtReader", "TxtPrevious", "TxtNext", "TxtPlayback" }), "all four TXT global hotkeys dispatch to independent reader actions");
                check(hotkeys.TrySet("TxtNext", "", out _) && !hotkeys.TrySet("EmergencyStop", "", out _), "optional reader shortcuts can be disabled while emergency stop stays available");
                check(!hotkeys.TrySet("TxtNext", new AppSettings().Hotkeys["TxtPrevious"], out var error) && error.Contains("分配"), "reader shortcut duplicates are detected");
            }
            hotkeyWindow.Close();
        }
        finally
        {
            AppController.ReaderWindow?.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
            // Only this freshly created test directory is removed.
            Directory.Delete(directory, true);
        }
    }

    private static void CheckTextLocation(string directory, Action<bool, string> check)
    {
        var settings = new TxtReaderSettings { PauseOnManualNavigation = true };
        var saves = 0;
        using var reader = new TxtReaderService(settings, () => { saves++; return Task.CompletedTask; });
        const string shortest = "这是一段用于文字定位的原文内容。";
        var ten = shortest[..10];
        check(!reader.TrySeekText(ten, out var message) && message.Contains("先打开"), "text location explains when no TXT is loaded");
        var thirty = string.Concat(Enumerable.Repeat("中文定位测试", 5));
        var unicode = string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦a\u0301", 5));
        var prefix = "开头前缀。";
        var text = prefix + shortest + "中间。" + thirty + "中间。" + unicode + "结尾。" + shortest;
        var path = Path.Combine(directory, "text-location.txt"); File.WriteAllText(path, text);
        check(Wait(reader.OpenAsync(path)), "text-location sample loads");
        reader.Seek(text.Length - 1); reader.Play(); var beforeSaves = saves;
        check(reader.TrySeekText(ten, out message) && reader.Offset == prefix.Length && reader.CurrentPage!.Text.StartsWith(ten, StringComparison.Ordinal)
            && !reader.IsPlaying && settings.RecentFiles.Single().Offset == prefix.Length && saves > beforeSaves,
            "ten-character text location finds the first match across the full document, updates the page, pauses and saves progress");
        check(reader.TrySeekText(thirty, out _) && reader.Offset == text.IndexOf(thirty, StringComparison.Ordinal), "text location accepts the inclusive thirty-character upper bound");
        check(reader.TrySeekText(unicode, out _) && reader.Offset == text.IndexOf(unicode, StringComparison.Ordinal), "text location counts emoji clusters and combining marks as single characters");
        settings.PauseOnManualNavigation = false; reader.Play();
        check(reader.TrySeekText(ten, out _) && reader.IsPlaying, "text location respects the option to continue automatic reading after manual navigation");
        var offset = reader.Offset; beforeSaves = saves;
        foreach (var invalid in new[] { "", new string(' ', 10), ten[..9], thirty + "字", "原文不存在的十个字测试" })
            check(!reader.TrySeekText(invalid, out message) && !string.IsNullOrWhiteSpace(message) && reader.Offset == offset && reader.IsPlaying && saves == beforeSaves,
                "invalid or unmatched text leaves the reading position, playback and saved progress intact");
        reader.TrySeekText("原文不存在的十个字测试", out message);
        check(message.Contains("未找到"), "unmatched text reports a clear result");
    }

    private static void CheckTextLocator(Window window, TxtReaderService reader, Action<bool, string> check)
    {
        var progress = Descendants<GroupBox>((DependencyObject)window.Content).Single(group => (string)group.Header == "阅读进度");
        var input = Descendants<TextBox>(progress).Single(box => box.Name == "ReaderTextSearchInput");
        var locate = Descendants<Button>(progress).Single(button => button.Name == "ReaderTextLocateButton");
        var originalOffset = reader.Offset;
        try
        {
            input.Text = "第一段中文，Hell";
            locate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(reader.Offset == 0, "text locator at the bottom of reading progress navigates when its button is clicked");
            reader.Seek(originalOffset);
            using var source = new HwndSource(new HwndSourceParameters("text locator regression"));
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            check(reader.Offset == 0, "Enter in the text locator performs the same full-document search");
            input.Text = "少于十字"; reader.Seek(originalOffset);
            locate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(reader.Offset == originalOffset, "the text locator rejects input shorter than ten characters");
            input.Text = new string('字', 30); input.Select(input.Text.Length, 0);
            var typed = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, input, "字")) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            input.RaiseEvent(typed);
            check(typed.Handled, "typing beyond thirty characters is blocked");
            var pasted = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, "字"), false, DataFormats.UnicodeText);
            input.RaiseEvent(pasted);
            check(pasted.CommandCancelled, "pasting beyond thirty characters is rejected");
            input.SelectAll();
            var replacement = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, "第一段中文，Hell"), false, DataFormats.UnicodeText);
            input.RaiseEvent(replacement);
            check(!replacement.CommandCancelled, "pasting valid replacement text accounts for the current selection");
        }
        finally { input.Clear(); reader.Seek(originalOffset); }
    }

    private static void CheckColorPalette(Window window, TxtReaderService reader, Action<bool, string> check)
    {
        var settings = reader.Settings; var originalColor = settings.TextColor; var offset = reader.Offset;
        var picker = Descendants<Button>((DependencyObject)window.Content).Single(button => button.Name == "ReaderColorPickerButton");
        var popup = (System.Windows.Controls.Primitives.Popup)typeof(TxtReaderSettingsWindow).GetField("_colorPopup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(60); RenderElement((FrameworkElement)popup.Child, "single-line-color-palette.png");
        Descendants<Button>(popup.Child).Single(button => button.Name == "ReaderQuickColorE05252").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!popup.IsOpen && settings.TextColor == "#E05252" && reader.Offset == offset,
            "compact palette applies a selected color immediately and closes without changing reading progress");
        picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(40);
        var hue = Descendants<Slider>(popup.Child).Single(); hue.Value = 200;
        check(popup.IsOpen && settings.TextColor != "#E05252" && reader.Offset == offset,
            "hue selection previews the text color live without closing the color field or restarting reading");
        var preview = settings.TextColor; popup.IsOpen = false;
        picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(40); popup.IsOpen = false;
        check(settings.TextColor == preview, "opening and dismissing the palette without selecting preserves the current color");
        NativeMethods.GetCursorPos(out var cursor);
        try
        {
            picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(40);
            var huePoint = hue.PointToScreen(new Point(hue.ActualWidth * .2, hue.ActualHeight / 2));
            NativeDrag((int)huePoint.X, (int)huePoint.Y, 0, 0); Pump(40);
            check(popup.IsOpen && Math.Abs(hue.Value - 72) < 15 && settings.TextColor != preview,
                "real mouse selection on the custom hue track updates the color");
            var field = Descendants<Grid>(popup.Child).Single(grid => grid.Width == 224 && grid.Height == 130);
            var point = field.PointToScreen(new Point(140, 35)); var before = settings.TextColor;
            NativeDrag((int)point.X, (int)point.Y, 20, 15); Pump(40);
            check(!popup.IsOpen && settings.TextColor != before && reader.Offset == offset,
                "real mouse dragging in the color field applies and commits the selected color");
        }
        finally { popup.IsOpen = false; SetCursorPos(cursor.X, cursor.Y); }
        settings.TextColor = originalColor; reader.RefreshLayout(false);
    }

    private static void CheckFontStepper(Window window, TxtReaderService reader, Action<bool, string> check)
    {
        var original = reader.Settings.FontSize; var offset = reader.Offset;
        var arrows = Descendants<RepeatButton>((DependencyObject)window.Content).ToArray();
        var up = arrows.Single(button => button.Name == "ReaderFontSizeIncrease");
        var down = arrows.Single(button => button.Name == "ReaderFontSizeDecrease");
        up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(reader.Settings.FontSize == original + 1 && reader.Offset == offset, "font stepper increases size immediately while preserving the reading bookmark");
        down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(reader.Settings.FontSize == original, "font stepper decreases size immediately");
        reader.Settings.FontSize = 72; reader.RefreshLayout();
        check(!up.IsEnabled && down.IsEnabled, "font stepper disables increasing at the maximum size");
        down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        reader.Settings.FontSize = 10; reader.RefreshLayout();
        check(!down.IsEnabled && up.IsEnabled, "font stepper disables decreasing at the minimum size");
        up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(reader.Settings.FontSize == 11 && reader.Offset == offset, "font stepper resumes normally after reaching a size limit");
        reader.Settings.FontSize = original; reader.RefreshLayout();
    }

    private static void CheckMouseInteraction(TxtReaderWindow window, TxtReaderService reader, Action<bool, string> check)
    {
        var handle = new WindowInteropHelper(window).Handle;
        NativeMethods.GetCursorPos(out var originalCursor); var foreground = NativeMethods.GetForegroundWindow();
        try
        {
            NativeMethods.GetWindowRect(handle, out var rect);
            var blankX = rect.Right - 12; var blankY = (rect.Top + rect.Bottom) / 2;
            check(WindowFromPoint(new NativeMethods.NativePoint { X = blankX, Y = blankY }) == handle,
                "transparent blank area receives native mouse input instead of passing through to the desktop");
            if (!SetCursorPos(blankX, blankY)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Mouse interaction regression requires access to the interactive desktop.");
            Pump(100);
            check(window.HoverToolbar.IsOpen, "hovering a transparent blank area automatically opens the toolbar");
            var toolbarButtons = Descendants<Button>(window.HoverToolbar.Child).ToArray();
            check(toolbarButtons.Length == 8, "hover toolbar offers previous, start-stop, next, mode, lock, pin, settings and hide");
            RenderElement((FrameworkElement)window.HoverToolbar.Child, "single-line-hover-toolbar.png");
            var next = toolbarButtons.Single(button => (string)button.ToolTip == "下一页");
            var previous = toolbarButtons.Single(button => (string)button.ToolTip == "上一页");
            reader.Seek(0); var nextOffset = reader.CurrentPage!.End;
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(reader.Offset == nextOffset, "hover next-page action advances by the exact displayed segment");
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(reader.Offset == 0, "hover previous-page action returns to the prior segment");
            var playback = toolbarButtons.Single(button => (string)button.ToolTip == "开始 / 停止自动阅读");
            playback.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(reader.IsPlaying, "hover start action starts automatic reading");
            playback.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(!reader.IsPlaying, "hover stop action stops automatic reading");
            var originalMode = reader.Settings.Mode;
            var mode = toolbarButtons.Single(button => (string)button.ToolTip == "切换静态替换 / 平滑滑动");
            mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(reader.Settings.Mode != originalMode && reader.Offset == 0, "hover mode switch retains the original-text cursor");
            mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CheckToolbarAdjustments(window, reader, check);
            var pin = toolbarButtons.Single(button => (string)button.ToolTip == "开启 / 取消置顶");
            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(!window.Topmost && !reader.Settings.Topmost, "hover pin action updates the actual window and saved setting");
            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // Move from the reading strip onto the popup, then away from both surfaces.
            var point = ((FrameworkElement)window.HoverToolbar.Child).PointToScreen(new Point(8, 8));
            SetCursorPos((int)point.X, (int)point.Y); Pump(280);
            check(window.HoverToolbar.IsOpen, "toolbar remains open while moving the pointer from text onto its menu");
            var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            SetCursorPos(area.Left + 4, area.Top + 4); Pump(280);
            check(!window.HoverToolbar.IsOpen, "hover toolbar hides after the pointer leaves both surfaces");

            NativeMethods.GetWindowRect(handle, out var before);
            NativeDrag(before.Right - 12, (before.Top + before.Bottom) / 2, -65, -24); Pump(60);
            NativeMethods.GetWindowRect(handle, out var after);
            check(Math.Abs(after.Left - before.Left) >= 40 && Math.Abs(after.Top - before.Top) >= 10,
                "a real mouse drag on transparent blank space moves the entire reading window");
            var lockButton = toolbarButtons.Single(button => (string)button.ToolTip == "锁定 / 解锁拖动");
            lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            NativeDrag(after.Right - 12, (after.Top + after.Bottom) / 2, -50, -18); Pump(60);
            NativeMethods.GetWindowRect(handle, out var locked);
            check(reader.Settings.Locked && locked.Left == after.Left && locked.Top == after.Top,
                "locking disables blank-area dragging while retaining hover controls");
            lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.SetDefaultPosition();
        }
        finally { SendMouse(4); SetCursorPos(originalCursor.X, originalCursor.Y); if (foreground != nint.Zero) NativeMethods.SetForegroundWindow(foreground); }
    }

    private static void CheckToolbarAdjustments(TxtReaderWindow window, TxtReaderService reader, Action<bool, string> check)
    {
        var settings = reader.Settings;
        var originalMode = settings.Mode; var originalSeconds = settings.SecondsPerPage; var originalSpeed = settings.PixelsPerSecond;
        var originalFont = settings.FontSize; var originalHeight = settings.Height; var originalOffset = reader.Offset;
        var originalTextOpacity = settings.TextOpacity; var originalBackgroundOpacity = settings.BackgroundOpacity;
        var originalWheelPaging = settings.MouseWheelPaging;
        NativeMethods.GetCursorPos(out var cursor);
        var buttons = Descendants<RepeatButton>(window.HoverToolbar.Child).ToArray();
        var speedUp = buttons.Single(button => button.Name == "ReaderToolbarSpeedIncrease");
        var speedDown = buttons.Single(button => button.Name == "ReaderToolbarSpeedDecrease");
        var fontUp = buttons.Single(button => button.Name == "ReaderToolbarFontSizeIncrease");
        var fontDown = buttons.Single(button => button.Name == "ReaderToolbarFontSizeDecrease");
        var speedBox = Descendants<TextBox>(window.HoverToolbar.Child).Single(box => box.Name == "ReaderToolbarSpeedInput");
        var mode = Descendants<Button>(window.HoverToolbar.Child).Single(button => (string)button.ToolTip == "切换静态替换 / 平滑滑动");
        try
        {
            settings.Mode = "Static"; settings.SecondsPerPage = 6; settings.PixelsPerSecond = 40; reader.RefreshLayout();
            var editPoint = speedBox.PointToScreen(new Point(9, speedBox.ActualHeight / 2));
            NativeDrag((int)editPoint.X, (int)editPoint.Y, 0, 0); Pump(40);
            check(speedBox.IsKeyboardFocusWithin, "clicking the hover speed text acquires keyboard focus for manual input");
            TypeNumber(speedBox, "12");
            check(settings.SecondsPerPage == 12 && reader.Offset == originalOffset, "real keyboard typing and Enter apply the hover speed value");
            TypeNumber(speedBox, "999");
            check(settings.SecondsPerPage == 12 && speedBox.Text == "12", "out-of-range hover input restores the previous valid value");
            TypeNumber(speedBox, "8", false);
            var fontBox = Descendants<TextBox>(window.HoverToolbar.Child).Single(box => box.Name == "ReaderToolbarFontSizeInput");
            var fontEditPoint = fontBox.PointToScreen(new Point(7, fontBox.ActualHeight / 2));
            NativeDrag((int)fontEditPoint.X, (int)fontEditPoint.Y, 0, 0); Pump(40);
            check(settings.SecondsPerPage == 8 && fontBox.IsKeyboardFocusWithin, "leaving the hover speed field applies typed text and allows editing the font field");
            TypeNumber(fontBox, "26");
            check(settings.FontSize == 26 && reader.Offset == originalOffset && window.HoverToolbar.IsOpen, "real keyboard input changes the hover font size without moving the bookmark");
            CheckToolbarClickCommit(window, reader, check);
            Keyboard.Focus((IInputElement)window.Content); Pump(40);
            settings.FontSize = originalFont; settings.Height = originalHeight; settings.SecondsPerPage = 6; reader.RefreshLayout();
            NativeWheel(speedBox, 120);
            check(settings.SecondsPerPage == 7 && !speedBox.IsKeyboardFocusWithin && reader.Offset == originalOffset, "hovered speed input responds to a real mouse wheel without keyboard focus or page navigation");
            NativeWheel(speedBox, -120); NativeWheel(fontBox, 120);
            check(settings.FontSize == originalFont + 1 && reader.Offset == originalOffset, "hovered font input responds to a real mouse wheel without navigating the text");
            NativeWheel(fontBox, -120);
            speedUp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(settings.SecondsPerPage == 7 && reader.Offset == originalOffset, "hover speed arrow adjusts seconds per page in static mode without moving the bookmark");
            speedDown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(settings.Mode == "Scroll" && speedBox.Text == "40" && Descendants<TextBlock>(window.HoverToolbar.Child).Any(text => text.Text == "px/s"),
                "hover speed editor switches to the independent pixel speed and unit in scrolling mode");
            reader.Play(); reader.Advance(.1); var before = reader.ScrollOffset;
            speedUp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(settings.PixelsPerSecond == 41 && reader.IsPlaying && reader.ScrollOffset == before && reader.Offset == originalOffset,
                "hover speed change preserves playback and the precise scrolling position");
            reader.Advance(.1);
            check(Math.Abs(reader.ScrollOffset - before - 4.1) < .001, "hover speed change takes effect on the next scrolling frame"); reader.Pause();
            settings.PixelsPerSecond = 200; reader.RefreshLayout(false); var upperBound = !speedUp.IsEnabled && speedDown.IsEnabled;
            settings.PixelsPerSecond = 10; reader.RefreshLayout(false);
            check(upperBound && !speedDown.IsEnabled && speedUp.IsEnabled, "hover scrolling speed arrows enforce both limits");
            mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(speedBox.Text == "6" && Descendants<TextBlock>(window.HoverToolbar.Child).Any(text => text.Text == "秒/屏"),
                "returning to static mode restores seconds per page and its unit");
            var point = speedUp.PointToScreen(new Point(speedUp.ActualWidth / 2, speedUp.ActualHeight / 2));
            Wait(Task.Run(async () =>
            {
                SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(45); SendMouse(2);
                try { await Task.Delay(620); } finally { SendMouse(4); }
            })); Pump(40);
            check(settings.SecondsPerPage >= 8 && window.HoverToolbar.IsOpen, "holding the hover speed arrow repeats adjustments and keeps the menu open");
            var fontPoint = fontUp.PointToScreen(new Point(fontUp.ActualWidth / 2, fontUp.ActualHeight / 2));
            NativeDrag((int)fontPoint.X, (int)fontPoint.Y, 0, 0); Pump(40);
            check(settings.FontSize == originalFont + 1 && reader.Offset == originalOffset && window.HoverToolbar.IsOpen,
                $"real mouse click on the hover font arrow changes the visible font without dragging or closing the menu (font={settings.FontSize}, expected={originalFont + 1}, offset={reader.Offset}, open={window.HoverToolbar.IsOpen})");
            fontDown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(settings.FontSize == originalFont, "hover font down arrow decreases the font immediately");

            var textOpacityBox = Descendants<TextBox>(window.HoverToolbar.Child).Single(box => box.Name == "ReaderToolbarTextOpacityInput");
            var backgroundUp = buttons.Single(button => button.Name == "ReaderToolbarBackgroundOpacityIncrease");
            settings.MouseWheelPaging = true;
            NativeWheel(textOpacityBox, -120);
            check(Math.Abs(settings.TextOpacity - .99) < .001 && settings.BackgroundOpacity == originalBackgroundOpacity && reader.Offset == originalOffset,
                "mouse wheel changes only text opacity without turning the page");
            var backgroundPoint = backgroundUp.PointToScreen(new Point(backgroundUp.ActualWidth / 2, backgroundUp.ActualHeight / 2));
            NativeDrag((int)backgroundPoint.X, (int)backgroundPoint.Y, 0, 0); Pump(40);
            check(Math.Abs(settings.BackgroundOpacity - .01) < .001 && Math.Abs(settings.TextOpacity - .99) < .001
                && reader.Offset == originalOffset && window.HoverToolbar.IsOpen,
                "mouse click on the background opacity arrow changes only the background and keeps the toolbar open");

            settings.SecondsPerPage = 6; reader.RefreshLayout(false);
            RenderElement((FrameworkElement)window.HoverToolbar.Child, "single-line-hover-toolbar-static.png");
            settings.PixelsPerSecond = 40; mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RenderElement((FrameworkElement)window.HoverToolbar.Child, "single-line-hover-toolbar-scroll.png");
        }
        finally
        {
            Keyboard.Focus((IInputElement)window.Content);
            reader.Pause(); settings.Mode = originalMode; settings.SecondsPerPage = originalSeconds; settings.PixelsPerSecond = originalSpeed;
            settings.TextOpacity = originalTextOpacity; settings.BackgroundOpacity = originalBackgroundOpacity; settings.MouseWheelPaging = originalWheelPaging;
            settings.FontSize = originalFont; settings.Height = originalHeight; reader.Seek(originalOffset); reader.RefreshLayout();
            SetCursorPos(cursor.X, cursor.Y); Pump(40);
        }
    }

    private static void CheckToolbarClickCommit(TxtReaderWindow window, TxtReaderService reader, Action<bool, string> check)
    {
        var settings = reader.Settings; var originalFont = settings.FontSize; var originalHeight = settings.Height;
        var originalSeconds = settings.SecondsPerPage; var originalMode = settings.Mode; var offset = reader.Offset;
        var panel = (FrameworkElement)window.HoverToolbar.Child;
        var speed = Descendants<TextBox>(panel).Single(box => box.Name == "ReaderToolbarSpeedInput");
        var font = Descendants<TextBox>(panel).Single(box => box.Name == "ReaderToolbarFontSizeInput");
        void Edit(TextBox box, string value)
        {
            var surface = (FrameworkElement)window.Content;
            var hover = surface.PointToScreen(new Point(surface.ActualWidth - 12, surface.ActualHeight / 2));
            SetCursorPos((int)hover.X, (int)hover.Y); Pump(100);
            var point = box.PointToScreen(new Point(7, box.ActualHeight / 2));
            NativeDrag((int)point.X, (int)point.Y, 0, 0); Pump(40); TypeNumber(box, value, false);
        }
        System.Diagnostics.Process? child = null;
        try
        {
            Edit(font, "28");
            var blank = panel.PointToScreen(new Point(5, panel.ActualHeight - 7));
            NativeDrag((int)blank.X, (int)blank.Y, 0, 0); Pump(40);
            check(settings.FontSize == 28, "clicking empty hover-menu space commits typed font size without Enter");
            Edit(speed, "14");
            var mode = Descendants<Button>(panel).Single(button => (string)button.ToolTip == "切换静态替换 / 平滑滑动");
            var modePoint = mode.PointToScreen(new Point(mode.ActualWidth / 2, mode.ActualHeight / 2));
            NativeDrag((int)modePoint.X, (int)modePoint.Y, 0, 0); Pump(40);
            check(settings.SecondsPerPage == 14 && settings.Mode == "Scroll", "clicking a hover action commits seconds before switching to the independent scrolling speed");
            mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--native-targets")
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true })!;
            var handles = Wait(child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!.Split(':').Select(long.Parse).ToArray();
            var outside = new nint(handles[0]);
            NativeMethods.SetWindowPos(outside, NativeMethods.HwndTopmost, 20, 20, 240, 160, NativeMethods.SwpNoActivate);
            void ClickOutside()
            {
                NativeMethods.GetWindowRect(outside, out var rect); NativeDrag(rect.Left + 30, rect.Top + 80, 0, 0); Pump(100);
                if (NativeMethods.GetForegroundWindow() != outside) throw new InvalidOperationException("Cross-process commit test did not activate its independent target.");
            }
            Edit(speed, "13"); ClickOutside();
            check(settings.SecondsPerPage == 13 && reader.Offset == offset, "clicking another application commits typed speed without Enter or moving the bookmark");
            Edit(font, "29"); ClickOutside();
            check(settings.FontSize == 29 && reader.Offset == offset, "clicking another application commits typed font size without Enter");
            Edit(font, "999"); ClickOutside();
            check(settings.FontSize == 29 && font.Text == "29", "clicking elsewhere restores invalid input to the last valid font size");
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited)
                {
                    child.StandardInput.WriteLine("exit"); child.StandardInput.Flush();
                    if (!child.WaitForExit(3000)) child.Kill();
                }
                child.Dispose();
            }
            window.Activate(); Keyboard.Focus((IInputElement)window.Content);
            settings.Mode = originalMode; settings.SecondsPerPage = originalSeconds; settings.FontSize = originalFont; settings.Height = originalHeight; reader.RefreshLayout();
            var surface = (FrameworkElement)window.Content;
            var hover = surface.PointToScreen(new Point(surface.ActualWidth - 12, surface.ActualHeight / 2));
            SetCursorPos((int)hover.X, (int)hover.Y); Pump(100);
        }
    }

    private static void CheckSettingsWheel(Window window, TabControl tabs, TxtReaderService reader, Action<bool, string> check)
    {
        TextBox Input(string caption) => Descendants<TextBox>((DependencyObject)window.Content).Single(box => System.Windows.Automation.AutomationProperties.GetName(box) == caption);
        void Change(string caption, Func<double> read, double expected)
        {
            var before = read(); var box = Input(caption);
            var handled = Wheel(box, 120);
            check(handled && Math.Abs(read() - expected) < .001, "settings wheel adjusts " + caption + " while consuming the input event");
            Wheel(box, -120);
            check(Math.Abs(read() - before) < .001, "settings wheel reverses " + caption + " adjustment");
        }
        var settings = reader.Settings; var offset = reader.Offset;
        tabs.SelectedIndex = 0; window.UpdateLayout(); Pump(40);
        Change("每屏秒数", () => settings.SecondsPerPage, settings.SecondsPerPage + 1);
        var mode = Descendants<ComboBox>((DependencyObject)window.Content).Single(box => box.Items.Cast<ComboBoxItem>().Any(item => (string)item.Tag == "Static"));
        Wheel(mode, -120); window.UpdateLayout();
        check(settings.Mode == "Scroll", "wheel over a closed reading-mode selector switches options");
        Change("速度 px/s", () => settings.PixelsPerSecond, settings.PixelsPerSecond + 1); Wheel(mode, 120);
        var progress = Descendants<GroupBox>((DependencyObject)window.Content).Single(group => (string)group.Header == "阅读进度");
        var progressInputs = Descendants<TextBox>(progress).ToArray(); var beforeProgress = progressInputs.Select(box => box.Text).ToArray();
        foreach (var box in progressInputs) { Wheel(box, 120); Wheel(box, -120); }
        check(progressInputs.Select(box => box.Text).SequenceEqual(beforeProgress) && reader.Offset == offset, "reading-progress inputs retain their values and bookmark when wheeled");
        tabs.SelectedIndex = 1; window.UpdateLayout(); Pump(40);
        Change("最大字数", () => settings.MaxCharacters, settings.MaxCharacters + 1);
        Change("字号", () => settings.FontSize, settings.FontSize + 1);
        var textOpacity = settings.TextOpacity; settings.TextOpacity = .5; reader.RefreshLayout(false);
        Change("文字", () => settings.TextOpacity * 100, 51);
        settings.TextOpacity = textOpacity; reader.RefreshLayout(false);
        Change("底板", () => settings.BackgroundOpacity * 100, settings.BackgroundOpacity * 100 + 1);
        Change("宽度", () => settings.Width, settings.Width + 1);
        Change("高度", () => settings.Height, settings.Height + 1);
        var capacity = settings.MaxCharacters; var max = Input("最大字数");
        Wheel(max, 60); var partial = settings.MaxCharacters; Wheel(max, 60);
        check(partial == capacity && settings.MaxCharacters == capacity + 1, "high-resolution wheel deltas accumulate into one adjustment");
        Wheel(max, -120); settings.MaxCharacters = 64; reader.RefreshLayout(); Wheel(max, 120);
        check(settings.MaxCharacters == 64, "wheel input stops at the maximum character limit");
        settings.MaxCharacters = capacity; reader.RefreshLayout();
        var font = Descendants<ComboBox>((DependencyObject)window.Content).Single(box => box.Name == "ReaderFontFamilyInput");
        var originalFont = settings.FontFamily; Wheel(font, -120);
        check(settings.FontFamily == "SimSun", "wheel over the common-font selector changes the family immediately");
        settings.FontFamily = originalFont; reader.RefreshLayout();
        var alignment = Descendants<ComboBox>((DependencyObject)window.Content).Single(box => box.Items.Cast<ComboBoxItem>().Any(item => (string)item.Tag == "Left"));
        Wheel(alignment, -120); check(settings.Alignment == "Center", "wheel over alignment switches to the next option"); Wheel(alignment, 120);
        check(reader.Offset == offset, "settings wheel adjustments preserve the original reading bookmark");
    }

    private static bool Wheel(UIElement element, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        element.RaiseEvent(args); return args.Handled;
    }
    private static void NativeWheel(FrameworkElement element, int delta)
    {
        var point = element.PointToScreen(new Point(7, element.ActualHeight / 2));
        SetCursorPos((int)point.X, (int)point.Y); Pump(60);
        NativeMethods.SendInput(1, [new() { Mouse = new() { Flags = 0x0800, MouseData = unchecked((uint)delta) } }], Marshal.SizeOf<NativeMethods.Input>()); Pump(60);
    }
    private static void TypeNumber(TextBox box, string value, bool commit = true)
    {
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var process);
        if (!box.IsKeyboardFocusWithin || process != Environment.ProcessId) throw new InvalidOperationException("Native keyboard test only types into the focused test window.");
        var inputs = new List<KeyboardTestInput>();
        void Key(ushort key, bool up = false) => inputs.Add(new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = up ? 2u : 0u } } });
        Key(0x11); Key(0x41); Key(0x41, true); Key(0x11, true);
        foreach (var digit in value) { Key(digit); Key(digit, true); }
        if (commit) { Key(0x0D); Key(0x0D, true); }
        if (SendKeyboardInputs((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<KeyboardTestInput>()) != inputs.Count)
            throw new InvalidOperationException("Native keyboard input was not delivered.");
        Pump(80);
    }
    [DllImport("user32.dll", EntryPoint = "SendInput")] private static extern uint SendKeyboardInputs(uint count, [In] KeyboardTestInput[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardTestInput { public uint Type; public KeyboardTestUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct KeyboardTestUnion
    {
        [FieldOffset(0)] public NativeMethods.MouseInput Mouse;
        [FieldOffset(0)] public KeyboardTestData Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardTestData { public ushort Key, Scan; public uint Flags, Time; public nint ExtraInfo; }

    private static void NativeDrag(int x, int y, int dx, int dy)
    {
        Wait(Task.Run(async () =>
        {
            SetCursorPos(x, y); await Task.Delay(45); SendMouse(2);
            try { await Task.Delay(70); SetCursorPos(x + dx, y + dy); await Task.Delay(90); }
            finally { SendMouse(4); }
        }));
    }
    private static void SendMouse(uint flags) => NativeMethods.SendInput(1, [new() { Mouse = new() { Flags = flags } }], Marshal.SizeOf<NativeMethods.Input>());
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativeMethods.NativePoint point);

    private static void CheckRenderingClock(TxtReaderService reader, Action<bool, string> check)
    {
        reader.Pause(); reader.Settings.Mode = "Scroll"; reader.Settings.PixelsPerSecond = 200;
        reader.Settings.MaxCharacters = 64;
        var measures = 0;
        reader.SetViewport(2000, text => { measures++; return new StringInfo(text).LengthInTextElements * 22; });
        foreach (var hz in new[] { 60, 120, 144 })
        {
            reader.Seek(0); reader.Play(); var before = measures;
            reader.AdvanceRendering(TimeSpan.Zero);
            for (var frame = 1; frame <= hz; frame++)
            {
                var time = TimeSpan.FromSeconds((double)frame / hz);
                reader.AdvanceRendering(time); reader.AdvanceRendering(time);
            }
            check(Math.Abs(reader.ScrollOffset - 200) < .001, $"{hz} Hz rendering advances exactly one second at maximum speed and ignores duplicate frame callbacks");
            check(measures == before, $"{hz} Hz scrolling reuses measured page width between page changes");
            reader.Pause(); var offset = reader.ScrollOffset;
            reader.AdvanceRendering(TimeSpan.FromSeconds(2));
            check(reader.ScrollOffset == offset, "paused playback ignores compositor frames");
        }
        reader.Play(); var pausedOffset = reader.ScrollOffset;
        reader.AdvanceRendering(TimeSpan.FromSeconds(50));
        reader.AdvanceRendering(TimeSpan.FromSeconds(50 + 1d / 120));
        check(Math.Abs(reader.ScrollOffset - pausedOffset - 200d / 120) < .001, "resume resets the frame timestamp without consuming the paused duration");
        var beforeBlock = reader.ScrollOffset;
        reader.AdvanceRendering(TimeSpan.FromSeconds(80));
        check(reader.ScrollOffset == beforeBlock, "long compositor stalls do not skip unread content");
        reader.Settings.Mode = "Static"; reader.RefreshLayout();
        reader.AdvanceRendering(TimeSpan.FromSeconds(81));
        check(reader.ScrollOffset == 0, "static mode unsubscribes from smooth animation frames");
        reader.Pause(); reader.Settings.PixelsPerSecond = 40;
    }

    private static void RenderElement(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, name)); encoder.Save(stream);
        Console.WriteLine("RENDER: " + stream.Name);
    }

    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var matchChild in Descendants<T>(child)) yield return matchChild;
        }
    }
    private sealed class WindowScope(Window window) : IDisposable
    { public Window Window { get; } = window; public void Dispose() { if (Window.IsVisible) Window.Close(); } }
}
