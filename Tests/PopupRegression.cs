using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using CatFish.Core;
using CatFish.Features.Popup;
using Forms = System.Windows.Forms;

internal static class PopupRegression
{
    public static void Run(Action<bool, string> check, Action<Window, string> render)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        check(new AppSettings() is { PopupOpacity: 1, PopupVideoOpacity: .84, PopupGradientHeight: 62, PopupSideMaskWidth: 7, PopupBlurRadius: 3 },
            "default mask appearance matches the requested 100, 84, 62, 7, 3 values");
        check(!new AppSettings().PopupCloseSelectedApplication, "closing the selected application is opt-in by default");
        var path = Path.Combine(AppContext.BaseDirectory, "popup-legacy.json");
        try
        {
            File.WriteAllText(path, """{"PopupWidth":380,"PopupHeight":240,"PopupAutoHideSeconds":8}""");
            var store = new SettingsStore();
            typeof(SettingsStore).GetField("_path", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(store, path);
            var migrated = store.Load();
            check(migrated.PopupWidth == 640 && migrated.PopupHeight == 670 && migrated.PopupAutoHideSeconds == 0,
                "old reminder defaults migrate to a persistent square advertisement");
            migrated.PopupAutoHideSeconds = 17; Task.Run(() => store.SaveAsync(migrated)).GetAwaiter().GetResult();
            check(store.Load().PopupAutoHideSeconds == 17, "new advertisement settings keep an explicit hide timeout");
            File.WriteAllText(path, """{"PopupWidth":400,"PopupHeight":530,"PopupLayoutVersion":1,"PopupGradientHeight":56}""");
            check(store.Load() is { PopupWidth: 640, PopupHeight: 670, PopupGradientHeight: 28, PopupLayoutVersion: 6 },
                "previous advertisement defaults migrate to a 640 by 640 background plus title bar");
            File.WriteAllText(path, """{"PopupWidth":460,"PopupHeight":610,"PopupLayoutVersion":1}""");
            check(store.Load() is { PopupWidth: 610, PopupHeight: 640 }, "old custom sizes migrate to a square background while retaining their width");
            File.WriteAllText(path, """{"PopupWidth":760,"PopupHeight":420,"PopupLayoutVersion":2,"PopupBackground":"unknown"}""");
            check(store.Load() is { PopupWidth: 670, PopupHeight: 700, PopupBackground: "shopping" }, "large old sizes migrate within square limits and unknown backgrounds fall back safely");
            File.WriteAllText(path, """{"PopupSideMaskWidth":999,"PopupVideoOpacity":-1}""");
            check(store.Load() is { PopupSideMaskWidth: 120, PopupVideoOpacity: 0 }, "new side-mask and video-opacity settings enforce their bounds");
            var appearance = new AppSettings { PopupSideMaskWidth = 27, PopupVideoOpacity = .45, PopupAspectRatioLocked = false, PopupBackground = "gaming", PopupCloseSelectedApplication = true };
            Task.Run(() => store.SaveAsync(appearance)).GetAwaiter().GetResult();
            check(store.Load() is { PopupSideMaskWidth: 27, PopupVideoOpacity: .45 }, "side-mask width and video opacity persist independently");
            check(store.Load() is { PopupAspectRatioLocked: false, PopupBackground: "gaming" }, "aspect lock and selected background persist");
            check(store.Load().PopupCloseSelectedApplication, "close-selected-application option persists");
            File.Delete(path);
            check(store.Load() is { PopupLayoutVersion: 6, PopupWidth: 640, PopupHeight: 670 }, "fresh configuration uses the current 1:1 layout version");
            check(store.Load() is { PopupOpacity: 1, PopupVideoOpacity: .84, PopupGradientHeight: 62, PopupSideMaskWidth: 7, PopupBlurRadius: 3 },
                "a fresh installation loads the requested default appearance");
            File.WriteAllText(path, """{"PopupLayoutVersion":6}""");
            check(store.Load() is { PopupOpacity: 1, PopupVideoOpacity: .84, PopupGradientHeight: 62, PopupSideMaskWidth: 7, PopupBlurRadius: 3 },
                "configuration files with omitted appearance fields use the new defaults");
            File.WriteAllText(path, """{"PopupLayoutVersion":6,"PopupOpacity":0.7,"PopupVideoOpacity":0.5,"PopupGradientHeight":20,"PopupSideMaskWidth":12,"PopupBlurRadius":9}""");
            check(store.Load() is { PopupOpacity: .7, PopupVideoOpacity: .5, PopupGradientHeight: 20, PopupSideMaskWidth: 12, PopupBlurRadius: 9 },
                "explicit saved custom appearance is not overwritten by the new defaults");
            File.WriteAllText(path, """{"PopupWidth":640,"PopupHeight":400,"PopupLayoutVersion":3,"PopupBackground":"legacy"}""");
            check(store.Load() is { PopupWidth: 640, PopupHeight: 670, PopupBackground: "legacy" }, "square migration retains the selected Taobao background");
            File.WriteAllText(path, """{"PopupWidth":640,"PopupHeight":510,"PopupLayoutVersion":4,"PopupBackground":"gaming"}""");
            check(store.Load() is { PopupWidth: 640, PopupHeight: 670, PopupLayoutVersion: 6, PopupBackground: "gaming" }, "previous 4:3 default migrates to square while retaining the selected style");
            File.WriteAllText(path, """{"PopupWidth":600,"PopupHeight":480,"PopupLayoutVersion":4}""");
            check(store.Load() is { PopupWidth: 600, PopupHeight: 630, PopupLayoutVersion: 6 }, "square migration corrects a custom old rectangle instead of preserving its wrong ratio");
            File.WriteAllText(path, """{"PopupWidth":400,"PopupHeight":300,"PopupLayoutVersion":5,"PopupAspectRatioLocked":true,"PopupBackground":"legacy","PopupVideoOpacity":0.84,"PopupGradientHeight":16}""");
            var corrected = store.Load();
            check(corrected is { PopupWidth: 400, PopupHeight: 430, PopupLayoutVersion: 6, PopupAspectRatioLocked: true, PopupBackground: "legacy", PopupVideoOpacity: 0.84, PopupGradientHeight: 16 },
                "the reported saved 400 by 300 configuration migrates to a square while retaining appearance settings");
            var correctedWindow = new AdvertisementPopupWindow(corrected);
            try
            {
                correctedWindow.Show(); Pump(100);
                var correctedImage = correctedWindow.AdvertisementBackground;
                check(correctedImage.ActualWidth == 400 && correctedImage.ActualHeight == 400, "migrated existing configuration actually displays a square image region");
                render(correctedWindow, "advertisement-migrated-400-square.png");
            }
            finally { correctedWindow.Close(); }
            corrected.PopupHeight = 300; corrected.PopupAspectRatioLocked = false;
            Task.Run(() => store.SaveAsync(corrected)).GetAwaiter().GetResult();
            check(store.Load() is { PopupWidth: 400, PopupHeight: 300, PopupLayoutVersion: 6, PopupAspectRatioLocked: false }, "migration runs once and later intentional unlocked sizes are respected");
        }
        finally { File.Delete(path); }

        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--popup-targets");
        using var child = Process.Start(start)!;
        var settings = new AppSettings();
        var originalSettings = AppController.Settings;
        typeof(AppController).GetProperty(nameof(AppController.Settings))!.SetValue(null, settings);
        var popup = new AdvertisementPopupWindow(settings);
        var originalForeground = NativeMethods.GetForegroundWindow();
        try
        {
            var line = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()!;
            var handles = line.Split(':').Select(long.Parse).Select(value => new nint(value)).ToArray();
            var target = new WindowTarget(handles[0], "Popup target A", "popup-test", child.Id);
            var second = new WindowTarget(handles[1], "Popup target B", "popup-test", child.Id);
            var originalStyle = NativeMethods.GetWindowLongPtr(target.Handle, NativeMethods.GwlStyle);
            var originalExtended = NativeMethods.GetWindowLongPtr(target.Handle, NativeMethods.GwlExStyle);
            var originalPlacement = Placement(target.Handle);
            popup.Show(); Pump(100);
            check(popup.Width == 640 && popup.Height == 670, "default window reserves a 640 by 640 background and a separate title bar");
            CheckSizeLink(check, render, popup);
            foreach (var corner in new[] { "BottomRight", "BottomLeft", "TopRight", "TopLeft" })
            {
                settings.PopupCorner = corner; popup.ApplySettings(); Pump(35);
                NativeMethods.GetWindowRect(new WindowInteropHelper(popup).Handle, out var placed);
                var work = MonitorService.GetMonitors()[0].WorkArea;
                check(Math.Abs((corner.EndsWith("Right") ? placed.Right : placed.Left) - (corner.EndsWith("Right") ? work.Right : work.Left)) <= 1
                    && Math.Abs((corner.StartsWith("Bottom") ? placed.Bottom : placed.Top) - (corner.StartsWith("Bottom") ? work.Bottom : work.Top)) <= 1,
                    corner + " advertisement touches both work-area edges without a gap");
            }
            settings.PopupCorner = "BottomRight"; popup.ApplySettings(); Pump(100);
            NativeMethods.GetWindowRect(new WindowInteropHelper(popup).Handle, out var initialBounds);
            NativeMethods.GetCursorPos(out var originalCursor);
            try
            {
                SetCursorPos(initialBounds.Left + 30, initialBounds.Top + 10); Pump(140);
                check(popup.HoverControlsVisible, "top hover reveals both the action menu and bottom hint");
                Capture(popup, "advertisement-hover.png");
                SetCursorPos(initialBounds.Left + 30, initialBounds.Top + 160); Pump(140);
                check(!popup.HoverControlsVisible, "moving to the advertisement body hides both menu and hint");
            }
            finally { SetCursorPos(originalCursor.X, originalCursor.Y); }
            check(!popup.AllowsTransparency && popup.Topmost && !popup.ShowInTaskbar,
                "advertisement supports a real native child window without a layered parent");
            var image = (Image)((Grid)((Grid)popup.Content).Children[1]).Children[0];
            check(image.Source is not null && Math.Abs(image.Source.Width / image.Source.Height - 1) < .00001,
                "default background has a true 1:1 source aspect ratio");
            check(Math.Abs(image.ActualWidth / image.ActualHeight - 1) < .00001, "default image area displays 1:1 without title-bar distortion");
            check(image.Stretch == Stretch.Fill, "unlocked rectangular layouts scale the complete advertisement without cropping");
            foreach (var background in PopupBackgrounds.Items)
            {
                var source = PopupBackgrounds.Load(background.Id);
                check(source.PixelWidth == source.PixelHeight, background.Id + " background pixels have an exact 1:1 ratio");
            }
            check(PopupBackgrounds.Items.Single(background => background.Id == "legacy").Name == "淘宝双11 · 红金盛典"
                && PopupBackgrounds.Items.All(background => !background.Name.Contains("原图裁剪")), "Taobao has its new display name and no theme uses the old crop label");
            render(popup, "advertisement-background-mask.png");
            using var emptyFrame = CaptureBitmap(popup);
            var embedded = popup.TryEmbed(target);
            var embedStatus = (TextBlock)typeof(AdvertisementPopupWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup)!;
            check(embedded, "a real external process window embeds successfully: " + embedStatus.Text); Pump(150);
            check(NativeMethods.GetParent(target.Handle) == popup.Host.Handle && NativeMethods.IsChild(new WindowInteropHelper(popup).Handle, target.Handle),
                "selected application is an actual child of the advertisement");
            NativeMethods.GetClientRect(popup.Host.Handle, out var hostBounds); NativeMethods.GetWindowRect(target.Handle, out var targetBounds);
            check(targetBounds.Right - targetBounds.Left == hostBounds.Right && targetBounds.Bottom - targetBounds.Top == hostBounds.Bottom,
                "embedded application's dimensions adapt to the available advertisement area");
            var caption = (TextBlock)typeof(AdvertisementPopupWindow).GetField("_caption", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup)!;
            check(caption.Text == "广告" && caption.ToolTip is null, "advertisement title does not show the selected application name");
            var overlay = (VideoEdgeOverlay)typeof(AdvertisementPopupWindow).GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup)!;
            var overlayHandle = new WindowInteropHelper(overlay).Handle;
            check(overlay.IsVisible && (NativeMethods.GetWindowLongPtr(overlayHandle, NativeMethods.GwlExStyle).ToInt64() & 0x20) != 0,
                "edge mask renders in a separate click-through window above native content");
            check(VisualTreeHelper.GetChildrenCount(overlay.Content as DependencyObject) > 0,
                "edge overlay has top and bottom gradient surfaces");
            var topImage = (Image)typeof(VideoEdgeOverlay).GetField("_topImage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
            check(topImage.Source is not null && topImage.Effect is System.Windows.Media.Effects.BlurEffect,
                "edge mask blurs its background image");
            check(ReferenceEquals(topImage.Source, image.Source), "mask uses the advertisement background and never samples the video");
            var styleWindow = new PopupSettingsWindow();
            try
            {
                Slider Field(string name) => (Slider)typeof(PopupSettingsWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(styleWindow)!;
                check(Field("_opacity").Value == 100 && Field("_videoOpacity").Value == 84 && Field("_gradient").Value == 62
                    && Field("_sideGradient").Value == 7 && Field("_blur").Value == 3, "all five settings sliders initialize to the requested default values");
                Field("_videoOpacity").Value = 100;
                Field("_gradient").Value = 96; Field("_blur").Value = 0; Pump(100);
                var topBand = (Grid)typeof(VideoEdgeOverlay).GetField("_top", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
                check(Math.Abs(topBand.Height - Math.Min(112, popup.Host.ActualHeight / 2)) < .01 && topImage.Effect is null, "height and blur sliders immediately update the live mask without saving");
                Field("_opacity").Value = 60;
                check(Math.Abs(topImage.Opacity - .6) < .001 && Math.Abs(image.Opacity - .6) < .001,
                    "opacity slider immediately affects both the background and covering mask");
                Field("_opacity").Value = 100; Pump(100);
                NativeMethods.GetWindowRect(target.Handle, out targetBounds);
                using var coveredFrame = CaptureBitmap(popup);
                var sampleX = (targetBounds.Left + targetBounds.Right) / 2 - initialBounds.Left;
                var topY = targetBounds.Top - initialBounds.Top + 12;
                var bottomY = targetBounds.Bottom - initialBounds.Top - 12;
                emptyFrame.Save(Path.Combine(AppContext.BaseDirectory, "advertisement-empty-frame.png"));
                coveredFrame.Save(Path.Combine(AppContext.BaseDirectory, "advertisement-covered-frame.png"));
                check(ColorDistance(emptyFrame.GetPixel(sampleX, topY), coveredFrame.GetPixel(sampleX, topY)) < 25
                    && ColorDistance(emptyFrame.GetPixel(sampleX, bottomY), coveredFrame.GetPixel(sampleX, bottomY)) < 25,
                    "native video is actually covered by matching advertisement pixels at both edges");
                Field("_gradient").Value = 0; Field("_sideGradient").Value = 32; Pump(100);
                var leftBand = (Grid)typeof(VideoEdgeOverlay).GetField("_left", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
                var rightBand = (Grid)typeof(VideoEdgeOverlay).GetField("_right", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
                check(leftBand.Width == 48 && rightBand.Width == 48 && overlay.IsVisible,
                    "left and right mask width updates live and remains independent of top and bottom height");
                using (var sideFrame = CaptureBitmap(popup))
                {
                    var sampleY = (targetBounds.Top + targetBounds.Bottom) / 2 - initialBounds.Top;
                    var leftX = targetBounds.Left - initialBounds.Left + 12;
                    var rightX = targetBounds.Right - initialBounds.Left - 12;
                    check(ColorDistance(emptyFrame.GetPixel(leftX, sampleY), sideFrame.GetPixel(leftX, sampleY)) < 25
                        && ColorDistance(emptyFrame.GetPixel(rightX, sampleY), sideFrame.GetPixel(rightX, sampleY)) < 25,
                        "left and right video edges are actually covered by matching advertisement pixels");
                }
                Field("_sideGradient").Value = 0; Pump(80);
                check(!overlay.IsVisible, "setting both mask dimensions to zero removes the overlay");
                var centerY = (targetBounds.Top + targetBounds.Bottom) / 2 - initialBounds.Top;
                using var opaqueVideo = CaptureBitmap(popup);
                var videoStyle = NativeMethods.GetWindowLongPtr(target.Handle, NativeMethods.GwlExStyle);
                Field("_videoOpacity").Value = 0; Pump(100);
                using (var transparentVideo = CaptureBitmap(popup))
                    check(overlay.IsVisible && ColorDistance(emptyFrame.GetPixel(sampleX, centerY), transparentVideo.GetPixel(sampleX, centerY)) < 25,
                        "zero video opacity reveals the matching advertisement background even with all edge masks disabled");
                Field("_videoOpacity").Value = 50; Pump(100);
                using (var blendedVideo = CaptureBitmap(popup))
                {
                    var backgroundColor = emptyFrame.GetPixel(sampleX, centerY); var videoColor = opaqueVideo.GetPixel(sampleX, centerY);
                    var expected = System.Drawing.Color.FromArgb((backgroundColor.R + videoColor.R) / 2, (backgroundColor.G + videoColor.G) / 2, (backgroundColor.B + videoColor.B) / 2);
                    check(ColorDistance(expected, blendedVideo.GetPixel(sampleX, centerY)) < 30,
                        "half video opacity composites the real native video and advertisement background at equal weights");
                }
                check(NativeMethods.GetWindowLongPtr(target.Handle, NativeMethods.GwlExStyle) == videoStyle,
                    "video transparency does not alter the third-party application's native transparency styles");
                Capture(popup, "advertisement-video-opacity.png");
                Field("_videoOpacity").Value = 100; Pump(80);
                check(!overlay.IsVisible, "full video opacity restores the uncovered video when all masks are disabled");
                Field("_sideGradient").Value = 16;
                Field("_gradient").Value = 28; Field("_blur").Value = 12; Pump(100);
                check(overlay.IsVisible && topImage.Effect is System.Windows.Media.Effects.BlurEffect { Radius: 12 },
                    "restoring height and blur rebuilds the visible background mask");
                styleWindow.Show();
                var tabs = Find<TabControl>((DependencyObject)styleWindow.Content).Single(); tabs.SelectedIndex = 1;
                render(styleWindow, "popup-live-style.png"); styleWindow.Close();
            }
            finally { if (styleWindow.IsVisible) styleWindow.Close(); }
            CheckResizeDrawing(check, popup, target, overlay);
            var previousHandle = popup.Host.Session.Handle;
            foreach (var background in PopupBackgrounds.Items)
            {
                settings.PopupBackground = background.Id; popup.ApplySettings(); Pump(80);
                check(ReferenceEquals(image.Source, PopupBackgrounds.Load(background.Id)) && popup.Host.Session.Handle == previousHandle,
                    "background switch to " + background.Id + " updates the image without reparenting the video");
                check(ReferenceEquals(topImage.Source, image.Source), "background switch to " + background.Id + " updates the edge masks");
                Capture(popup, "advertisement-style-" + background.Id + ".png");
            }
            settings.PopupBackground = "shopping"; popup.ApplySettings();
            popup.CycleBackground();
            check(settings.PopupBackground == "gaming", "top toolbar background action cycles to the next style");
            settings.PopupBackground = "shopping"; popup.ApplySettings();
            var muteButton = (Button)typeof(AdvertisementPopupWindow).GetField("_muteButton", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(popup)!;
            muteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var audioTimeout = Stopwatch.StartNew();
            while (popup.Audio.SessionCount == 0 && audioTimeout.ElapsedMilliseconds < 4000) Pump(150);
            check(popup.Audio.IsMuted && popup.Audio.SessionCount > 0 && popup.Audio.AllSessionsMuted,
                $"mute button actually mutes the external process's live Windows audio session (requested={popup.Audio.IsMuted}, sessions={popup.Audio.SessionCount}, status={embedStatus.Text}, error={popup.Audio.LastError})");
            check((string)muteButton.Content == "取消静音", "mute button reflects the active audio state");
            NativeMethods.GetCursorPos(out var muteCursor);
            try
            {
                NativeMethods.GetWindowRect(new WindowInteropHelper(popup).Handle, out var muteBounds);
                SetCursorPos(muteBounds.Left + 20, muteBounds.Top + 10); Pump(130);
                Capture(popup, "advertisement-muted-toolbar.png");
            }
            finally { SetCursorPos(muteCursor.X, muteCursor.Y); }
            muteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!popup.Audio.IsMuted && popup.Audio.SessionCount == 0, "second mute click restores and releases the original audio sessions");
            muteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            settings.PopupAutoHideSeconds = 1; popup.ApplySettings();
            var miniTask = Task.Run(() => PlatformMiniPlayer.TryInvoke(target.Handle));
            var miniTimeout = Stopwatch.StartNew();
            while (!miniTask.IsCompleted && miniTimeout.ElapsedMilliseconds < 3000) Pump(30);
            check(miniTask.IsCompletedSuccessfully && miniTask.Result, "platform mini-player action invokes the exposed small-window button");
            var miniClick = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            check(miniClick == "mini", "the external platform receives the small-window action");
            Pump(1150); check(popup.IsVisible, "auto-hide is paused while an application is embedded");
            Capture(popup, "advertisement-embedded-background-mask.png");
            settings.PopupWidth = 460; settings.PopupHeight = 600; popup.ApplySettings(); Pump(100);
            NativeMethods.GetClientRect(popup.Host.Handle, out hostBounds); NativeMethods.GetWindowRect(target.Handle, out targetBounds);
            check(targetBounds.Right - targetBounds.Left == hostBounds.Right && targetBounds.Bottom - targetBounds.Top == hostBounds.Bottom,
                "resizing the advertisement resizes its embedded application");
            check(popup.TryEmbed(second), "selecting a replacement embeds the new application"); Pump(100);
            check(!popup.Audio.IsMuted && popup.Audio.SessionCount == 0, "replacing an embedded application restores its audio before switching");
            check(NativeMethods.GetParent(target.Handle) == nint.Zero && NativeMethods.GetWindowLongPtr(target.Handle, NativeMethods.GwlStyle) == originalStyle
                && NativeMethods.GetWindowLongPtr(target.Handle, NativeMethods.GwlExStyle) == originalExtended,
                "replacing an application restores the original parent and window styles");
            var restored = Placement(target.Handle);
            check(restored.NormalPosition.Left == originalPlacement.NormalPosition.Left && restored.NormalPosition.Top == originalPlacement.NormalPosition.Top
                && restored.NormalPosition.Right == originalPlacement.NormalPosition.Right && restored.NormalPosition.Bottom == originalPlacement.NormalPosition.Bottom,
                "replacement restores the previous application's original desktop placement");
            popup.ClearApp(); Pump(100);
            check(NativeMethods.GetParent(second.Handle) == nint.Zero && popup.Host.Visibility == Visibility.Collapsed && !overlay.IsVisible,
                "restore removes the embedded application and its overlay");
            NativeMethods.GetWindowRect(new WindowInteropHelper(popup).Handle, out var popupBounds);
            popup.AcceptWindowDrop(target.Handle, new() { X = popupBounds.Left - 10, Y = popupBounds.Top });
            check(popup.Host.Session.Handle == nint.Zero, "window drops outside the advertisement are ignored");
            popup.AcceptWindowDrop(target.Handle, new() { X = popupBounds.Left + 50, Y = popupBounds.Top + 100 }); Pump(100);
            check(popup.Host.Session.Handle == target.Handle, "a dragged external window dropped inside the advertisement embeds");
            popup.ClearApp();
            NativeMethods.GetCursorPos(out var cursor);
            try
            {
                SetCursorPos(popupBounds.Left + 70, popupBounds.Top + 180);
                child.StandardInput.WriteLine("drop"); child.StandardInput.Flush();
                Pump(300);
                check(popup.Host.Session.Handle == target.Handle, "native move-end hook accepts a window dragged onto the advertisement");
                NativeMethods.GetWindowRect(target.Handle, out targetBounds);
                settings.PopupVideoOpacity = .45; popup.ApplySettings(); Pump(80);
                SetCursorPos((targetBounds.Left + targetBounds.Right) / 2, (targetBounds.Top + targetBounds.Bottom) / 2);
                NativeMethods.SendInput(2, [new() { Mouse = new() { Flags = 2 } }, new() { Mouse = new() { Flags = 4 } }], Marshal.SizeOf<NativeMethods.Input>());
                Pump(150);
                var click = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                check(click == "click", "real mouse input passes through four edge masks and video transparency to the embedded application");
                settings.PopupVideoOpacity = 1; popup.ApplySettings();
            }
            finally { SetCursorPos(cursor.X, cursor.Y); }
            muteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(80);
            check(popup.Audio.IsMuted && popup.Audio.SessionCount > 0, "test application is muted before the advertisement closes");
            popup.Close(); Pump(100);
            check(!popup.Audio.IsMuted && popup.Audio.SessionCount == 0, "closing the advertisement restores and releases its audio sessions");
            check(NativeMethods.IsWindow(target.Handle) && NativeMethods.GetParent(target.Handle) == nint.Zero,
                "closing the advertisement restores the application without closing it");
            var recoveryPopup = new AdvertisementPopupWindow(new AppSettings());
            try
            {
                recoveryPopup.Show(); Pump(50);
                NativeMethods.ShowWindowAsync(target.Handle, NativeMethods.SwMinimize); Pump(100);
                var minimizedAttached = recoveryPopup.TryEmbed(target);
                var recoveryStatus = (TextBlock)typeof(AdvertisementPopupWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(recoveryPopup)!;
                check(minimizedAttached, "a minimized application can be embedded: " + recoveryStatus.Text); Pump(100);
                check(!NativeMethods.IsIconic(target.Handle), "embedded minimized application becomes visible in the advertisement");
                recoveryPopup.ClearApp(); Pump(100);
                check(NativeMethods.IsIconic(target.Handle), "restore preserves the application's original minimized state");
                NativeMethods.ShowWindowAsync(target.Handle, 3); Pump(100);
                check(recoveryPopup.TryEmbed(target), "a maximized application can be embedded"); Pump(100);
                recoveryPopup.ClearApp(); Pump(100);
                check(Placement(target.Handle).ShowCommand == 3, "restore preserves the application's original maximized state");
            }
            finally { recoveryPopup.Close(); }
            CheckNativeRestore(check, child, second);
            NativeMethods.SetWindowLongPtr(second.Handle, NativeMethods.GwlExStyle, new nint(originalExtended.ToInt64() | NativeMethods.WsExToolWindow));
            check(!WindowDiscoveryService.IsUserWindow(second.Handle) && WindowDiscoveryService.IsUserWindow(second.Handle, true),
                "mini-player tool windows are eligible only in the advertisement picker");
            check(!new EmbeddedWindowSession().Attach(target with { Handle = nint.Zero }, nint.Zero, out var error) && error.Length > 0,
                "invalid embedding targets fail with a visible explanation");
            CheckCloseApplication(check, child, target, second);
        }
        finally
        {
            if (popup.IsVisible) popup.Close();
            typeof(AppController).GetProperty(nameof(AppController.Settings))!.SetValue(null, originalSettings);
            if (NativeMethods.IsWindow(originalForeground)) NativeMethods.SetForegroundWindow(originalForeground);
            if (!child.HasExited)
            {
                child.StandardInput.WriteLine("exit"); child.StandardInput.Flush();
                if (!child.WaitForExit(3000)) child.Kill();
            }
        }
    }

    private static void CheckResizeDrawing(Action<bool, string> check, AdvertisementPopupWindow popup, WindowTarget target, VideoEdgeOverlay overlay)
    {
        var settings = AppController.Settings;
        var previous = (settings.PopupWidth, settings.PopupHeight, settings.PopupAspectRatioLocked, settings.PopupOpacity,
            settings.PopupVideoOpacity, settings.PopupGradientHeight, settings.PopupSideMaskWidth, settings.PopupBlurRadius);
        NativeMethods.GetCursorPos(out var cursor);
        try
        {
            SetCursorPos(0, 0);
            settings.PopupAspectRatioLocked = false; settings.PopupOpacity = 1; settings.PopupBlurRadius = 0;
            foreach (var (width, height) in new[] { (400, 430), (600, 630), (460, 600), (640, 670) })
            {
                popup.ClearApp();
                settings.PopupWidth = width; settings.PopupHeight = height; popup.ApplySettings(); Pump(80);
                using var expected = CaptureBitmap(popup);
                settings.PopupWidth = width == 400 ? 640 : 400; settings.PopupHeight = width == 400 ? 670 : 430;
                popup.ApplySettings(); popup.TryEmbed(target); Pump(80);
                settings.PopupVideoOpacity = 0; settings.PopupGradientHeight = 0; settings.PopupSideMaskWidth = 0;
                settings.PopupWidth = width; settings.PopupHeight = height; popup.ApplySettings(); Pump(150);
                NativeMethods.GetWindowRect(new WindowInteropHelper(popup).Handle, out var bounds);
                NativeMethods.GetWindowRect(popup.Host.Handle, out var host);
                NativeMethods.GetWindowRect(new WindowInteropHelper(overlay).Handle, out var covering);
                var root = (FrameworkElement)overlay.Content;
                check(covering.Left == host.Left && covering.Top == host.Top && covering.Right == host.Right && covering.Bottom == host.Bottom
                    && Math.Abs(root.ActualWidth - popup.Host.ActualWidth) < 1 && Math.Abs(root.ActualHeight - popup.Host.ActualHeight) < 1,
                    $"resizing to {width} by {height} synchronizes native overlay bounds and WPF surface size");
                using (var actual = CaptureBitmap(popup))
                {
                    var matches = true;
                    for (var column = 1; column <= 3; column++)
                        for (var row = 1; row <= 3; row++)
                        {
                            var sampleX = host.Left - bounds.Left + (host.Right - host.Left) * column / 4;
                            var sampleY = host.Top - bounds.Top + (host.Bottom - host.Top) * row / 4;
                            matches &= ColorDistance(expected.GetPixel(sampleX, sampleY), actual.GetPixel(sampleX, sampleY)) < 30;
                        }
                    check(matches, $"resizing to {width} by {height} refreshes the video background without touching any mask slider");
                }
                settings.PopupVideoOpacity = 1; settings.PopupGradientHeight = 62; settings.PopupSideMaskWidth = 7;
                popup.ApplySettings(); Pump(100);
                using var edges = CaptureBitmap(popup);
                var centerX = (host.Left + host.Right) / 2 - bounds.Left;
                var centerY = (host.Top + host.Bottom) / 2 - bounds.Top;
                var edgePoints = new[] { (centerX, host.Top - bounds.Top + 4), (centerX, host.Bottom - bounds.Top - 4),
                    (host.Left - bounds.Left + 4, centerY), (host.Right - bounds.Left - 4, centerY) };
                check(edgePoints.All(point => ColorDistance(expected.GetPixel(point.Item1, point.Item2), edges.GetPixel(point.Item1, point.Item2)) < 30),
                    $"all four background mask edges stay aligned after resizing to {width} by {height}");
                var clientWidth = host.Right - host.Left; var clientHeight = host.Bottom - host.Top;
                using var freshVideo = new System.Drawing.Bitmap(clientWidth, clientHeight);
                using (var graphics = System.Drawing.Graphics.FromImage(freshVideo))
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0, 0, clientWidth, clientHeight),
                    System.Drawing.Color.SeaGreen, System.Drawing.Color.DarkSlateBlue, 90))
                    graphics.FillRectangle(brush, 0, 0, clientWidth, clientHeight);
                var videoPoints = new[] { (clientWidth * 3 / 4, clientHeight / 3), (clientWidth * 3 / 4, clientHeight * 2 / 3) };
                check(videoPoints.All(point => ColorDistance(freshVideo.GetPixel(point.Item1, point.Item2),
                    edges.GetPixel(host.Left - bounds.Left + point.Item1, host.Top - bounds.Top + point.Item2)) < 20),
                    $"the real external video repaints its full new client area after resizing to {width} by {height}");
                Capture(popup, $"advertisement-resized-{width}-{height}.png");
            }
        }
        finally
        {
            (settings.PopupWidth, settings.PopupHeight, settings.PopupAspectRatioLocked, settings.PopupOpacity,
                settings.PopupVideoOpacity, settings.PopupGradientHeight, settings.PopupSideMaskWidth, settings.PopupBlurRadius) = previous;
            popup.ApplySettings(); Pump(80); SetCursorPos(cursor.X, cursor.Y);
        }
    }

    private static void CheckCloseApplication(Action<bool, string> check, Process child, WindowTarget first, WindowTarget second)
    {
        AdvertisementPopupWindow CreatePopup()
        {
            var window = new AdvertisementPopupWindow(new AppSettings { PopupWidth = 400, PopupHeight = 430, PopupCloseSelectedApplication = true });
            window.Show(); Pump(60);
            if (!window.TryEmbed(second)) throw new InvalidOperationException("Could not embed close-option test target");
            return window;
        }
        var restorePopup = CreatePopup();
        try
        {
            restorePopup.ClearApp(); Pump(80);
            check(NativeMethods.IsWindow(second.Handle), "restore does not close the application even when close-on-advertisement-close is enabled");
            restorePopup.TryEmbed(second);
            AdvertisementPopupWindow.CloseAll(); Pump(100);
            check(!restorePopup.IsVisible && NativeMethods.IsWindow(second.Handle) && NativeMethods.GetParent(second.Handle) == nint.Zero,
                "emergency/global restore keeps the selected application open regardless of the close option");
        }
        finally { if (restorePopup.IsVisible) AdvertisementPopupWindow.CloseAll(); }
        child.StandardInput.WriteLine("cancel-close"); child.StandardInput.Flush();
        check(child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult() == "cancel-close-ready",
            "external test application can cancel a normal close request");
        var cancelledPopup = CreatePopup(); cancelledPopup.Close(); Pump(150);
        check(!cancelledPopup.IsVisible && NativeMethods.IsWindow(second.Handle) && !child.HasExited,
            "the close option respects the application's save/cancel handling instead of force-killing its process");
        foreach (var behavior in new[] { "cancel", "tray" })
        {
            foreach (var state in new[] { "normal", "minimized", "hidden", "maximized" })
            {
                var prepared = ProbeCommand(child, $"prepare-close:{behavior}:{state}");
                var expected = state == "maximized" ? prepared.Current : prepared.Normal;
                var trayPopup = CreatePopup(); Pump(80);
                trayPopup.Close(); Pump(150);
                var report = ProbeCommand(child, "close-probe");
                check(report.CloseCount == 1 && report.AtClose is { IsMinimized: false, IsVisible: true, Parent: 0 },
                    $"{state} application receives one normal close request only after becoming a visible restored top-level window ({behavior})");
                check(report.AtClose is { } atClose && SameBounds(atClose, expected) && CurrentClientSize(atClose),
                    $"{state} application has its original desktop bounds and a matching WM_SIZE client area before close handling ({behavior})");
                if (behavior == "tray")
                    check(!report.Current.IsVisible && NativeMethods.IsWindow(second.Handle) && !child.HasExited,
                        $"{state} application's own close-to-tray handling hides its window without terminating its process");
                else
                    check(report.Current.IsVisible && !report.Current.IsMinimized && SameBounds(report.Current, expected) && CurrentClientSize(report.Current),
                        $"{state} application that cancels closing remains visible at its original desktop location with its full client area");
            }
        }
        child.StandardInput.WriteLine("allow-close"); child.StandardInput.Flush();
        if (child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult() != "allow-close-ready")
            throw new InvalidOperationException("External test application did not allow closing");
        var closePopup = CreatePopup(); closePopup.Close(); Pump(180);
        check(!NativeMethods.IsWindow(second.Handle), "closing an opted-in advertisement sends a real close request to the selected external window");
        check(NativeMethods.IsWindow(first.Handle) && !child.HasExited, "closing the selected window does not terminate other windows in the same process");
    }

    private static void CheckNativeRestore(Action<bool, string> check, Process child, WindowTarget target)
    {
        foreach (var state in new[] { "minimized", "maximized" })
        {
            var prepared = ProbeCommand(child, $"prepare-close:cancel:{state}");
            var popup = new AdvertisementPopupWindow(new AppSettings { PopupWidth = 400, PopupHeight = 430 });
            try
            {
                popup.Show(); Pump(60);
                check(popup.TryEmbed(target), $"{state} native-state probe embeds successfully"); Pump(80);
                popup.ClearApp(); Pump(100);
                var restored = ProbeCommand(child, "close-probe");
                check(restored.Current.Parent == 0 && restored.Current.IsMinimized == (state == "minimized")
                    && restored.Current.LastSizeType == (state == "minimized" ? 1 : 2),
                    $"restoring a {state} application restores its actual native state and delivers the matching WM_SIZE event");
                if (state == "maximized")
                    check(SameBounds(restored.Current, prepared.Current) && CurrentClientSize(restored.Current),
                        "restoring a maximized application restores its complete desktop bounds and client area");
                NativeMethods.ShowWindowAsync(target.Handle, NativeMethods.SwRestore); Pump(100);
                var normal = ProbeCommand(child, "close-probe");
                check(!normal.Current.IsMinimized && SameBounds(normal.Current, prepared.Normal) && CurrentClientSize(normal.Current),
                    $"restoring the recovered {state} application requires no manual resize to recover its original client area");
            }
            finally { popup.Close(); }
        }
    }

    private static ProbeReport ProbeCommand(Process child, string command)
    {
        child.StandardInput.WriteLine(command); child.StandardInput.Flush();
        var response = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<ProbeReport>(response ?? throw new InvalidOperationException("Native close probe exited"))
            ?? throw new InvalidOperationException("Native close probe returned no state");
    }

    private static bool SameBounds(ProbeState actual, ProbeState expected)
        => actual.Left == expected.Left && actual.Top == expected.Top && actual.Width == expected.Width && actual.Height == expected.Height;

    private static bool CurrentClientSize(ProbeState state)
        => state.ClientWidth > 0 && state.ClientHeight > 0 && state.ClientWidth == state.LastSizeWidth && state.ClientHeight == state.LastSizeHeight;

    private sealed record ProbeState(bool IsMinimized, bool IsVisible, long Parent, int Left, int Top, int Width, int Height,
        int ClientWidth, int ClientHeight, int LastSizeType, int LastSizeWidth, int LastSizeHeight);
    private sealed record ProbeReport(ProbeState Current, ProbeState Normal, ProbeState? AtClose, int CloseCount);

    private static NativeMethods.WindowPlacement Placement(nint handle)
    {
        var placement = new NativeMethods.WindowPlacement { Length = Marshal.SizeOf<NativeMethods.WindowPlacement>() };
        NativeMethods.GetWindowPlacement(handle, ref placement); return placement;
    }
    private static void CheckSizeLink(Action<bool, string> check, Action<Window, string> render, AdvertisementPopupWindow popup)
    {
        var window = new PopupSettingsWindow();
        try
        {
            TextBox Field(string name) => (TextBox)typeof(PopupSettingsWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            var width = Field("_width"); var height = Field("_height");
            var link = (System.Windows.Controls.Primitives.ToggleButton)typeof(PopupSettingsWindow).GetField("_aspectLock", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            var closeApplication = (CheckBox)typeof(PopupSettingsWindow).GetField("_closeApplication", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            check(closeApplication.IsChecked == false, "close-application checkbox starts disabled");
            closeApplication.IsChecked = true;
            check(AppController.Settings.PopupCloseSelectedApplication, "enabling the close-application checkbox updates the active setting");
            closeApplication.IsChecked = false;
            check(!AppController.Settings.PopupCloseSelectedApplication, "disabling the close-application checkbox restores keep-open behavior");
            check(link.IsChecked == true, "aspect chain is locked by default");
            width.Text = "600";
            check(height.Text == "630" && popup.Width == 600 && popup.Height == 630, "changing width updates height while excluding the title bar from the square ratio");
            height.Text = "400";
            check(width.Text == "370", "changing height updates width while excluding the title bar");
            width.Text = "600"; check(width.Text == "600" && height.Text == "630", "rounding does not change the width entered by the user");
            height.Text = "240"; check(width.Text == "288" && height.Text == "318", "locked square size respects both dimensions' reduced limits");
            width.Text = "900"; check(width.Text == "670" && height.Text == "700", "linked width is limited by the maximum allowed image height");
            link.IsChecked = false; width.Text = "600"; height.Text = "500";
            check(width.Text == "600" && height.Text == "500", "unlocked dimensions can be edited independently");
            link.IsChecked = true; width.Text = "720";
            check(height.Text == "594", "relocking captures the current custom image ratio without the title bar");
            width.Text = "7"; check(height.Text == "594", "incomplete input does not corrupt the linked dimension");
            link.IsChecked = false; width.Text = "400"; height.Text = "300";
            window.RestoreSquareSize();
            check(width.Text == "400" && height.Text == "430" && link.IsChecked == true
                && popup.AdvertisementBackground.ActualWidth == popup.AdvertisementBackground.ActualHeight,
                "restore-square action corrects a saved 4:3 window and relocks the actual image area");
            width.Text = "500";
            check(height.Text == "530" && popup.AdvertisementBackground.ActualWidth == 500 && popup.AdvertisementBackground.ActualHeight == 500,
                "resizing after restoring square keeps the displayed background exactly square");
            link.IsChecked = false; width.Text = "640"; height.Text = "670"; link.IsChecked = true;
            window.Show(); render(window, "popup-linked-dimensions.png");
            var tabs = Find<TabControl>((DependencyObject)window.Content).Single(); tabs.SelectedIndex = 2;
            render(window, "popup-background-selector.png");
        }
        finally { window.Close(); }
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Capture(Window window, string filename)
    {
        using var bitmap = CaptureBitmap(window);
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, filename), System.Drawing.Imaging.ImageFormat.Png);
    }
    private static int ColorDistance(System.Drawing.Color first, System.Drawing.Color second)
        => Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B);
    private static System.Drawing.Bitmap CaptureBitmap(Window window)
    {
        NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds);
        var bitmap = new System.Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        return bitmap;
    }
    public static void RunTargets()
    {
        using var soundStream = SilentWave();
        using var sound = new System.Media.SoundPlayer(soundStream); sound.Load(); sound.PlayLooping();
        using var first = new ClickProbe { Text = "Popup target A", StartPosition = Forms.FormStartPosition.Manual, Location = new(80, 80), Size = new(640, 500) };
        using var second = new CloseProbe { Text = "Popup target B", StartPosition = Forms.FormStartPosition.Manual, Location = new(120, 120), Size = new(500, 400) };
        first.Paint += (_, args) =>
        {
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(first.ClientRectangle, System.Drawing.Color.SeaGreen, System.Drawing.Color.DarkSlateBlue, 90);
            args.Graphics.FillRectangle(brush, first.ClientRectangle);
            using var font = new System.Drawing.Font("Microsoft YaHei UI", 22);
            args.Graphics.DrawString("视频画面 / LIVE", font, System.Drawing.Brushes.White, 25, first.ClientSize.Height / 2 - 25);
            for (var index = 0; index < 8; index++) args.Graphics.DrawEllipse(System.Drawing.Pens.LightGreen, index * 55, 5, 45, 45);
        };
        first.Shown += (_, _) => { second.Show(); Console.WriteLine($"{first.Handle.ToInt64()}:{second.Handle.ToInt64()}"); };
        var mini = new Forms.Button { Text = "小窗播放", AccessibleName = "小窗播放", Location = new(10, 65), Size = new(100, 28) };
        mini.Click += (_, _) => Console.WriteLine("mini"); first.Controls.Add(mini);
        _ = Task.Run(() =>
        {
            while (Console.ReadLine() is { } command)
            {
                if (command == "drop") first.BeginInvoke(new Action(() => NotifyWinEvent(0xB, first.Handle, 0, 0)));
                else if (command == "cancel-close") first.BeginInvoke(new Action(() => { second.CloseBehavior = "cancel"; Console.WriteLine("cancel-close-ready"); }));
                else if (command == "allow-close") first.BeginInvoke(new Action(() => { second.CloseBehavior = "allow"; Console.WriteLine("allow-close-ready"); }));
                else if (command.StartsWith("prepare-close:")) first.BeginInvoke(new Action(() =>
                {
                    var arguments = command.Split(':');
                    second.Prepare(arguments[1], arguments[2]);
                    Console.WriteLine(JsonSerializer.Serialize(second.Report()));
                }));
                else if (command == "close-probe") first.BeginInvoke(new Action(() => Console.WriteLine(JsonSerializer.Serialize(second.Report()))));
                else { first.BeginInvoke(new Action(() => { second.CloseBehavior = "allow"; second.Close(); first.Close(); })); break; }
            }
        });
        Forms.Application.Run(first);
        sound.Stop();
    }

    private static MemoryStream SilentWave()
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
        {
            writer.Write("RIFF"u8); writer.Write(16036); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(16000); writer.Write(new byte[16000]);
        }
        stream.Position = 0; return stream;
    }

    private sealed class ClickProbe : Forms.Form
    {
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x201) Console.WriteLine("click");
            base.WndProc(ref message);
        }
    }

    private sealed class CloseProbe : Forms.Form
    {
        internal string CloseBehavior = "allow";
        private ProbeState? _normal, _atClose;
        private int _closeCount, _lastSizeType, _lastSizeWidth, _lastSizeHeight;

        public void Prepare(string behavior, string state)
        {
            CloseBehavior = behavior; _closeCount = 0; _atClose = null;
            WindowState = Forms.FormWindowState.Normal;
            Bounds = new System.Drawing.Rectangle(120, 120, 500, 400);
            Show();
            _normal = Snapshot();
            if (state == "minimized") WindowState = Forms.FormWindowState.Minimized;
            else if (state == "maximized") WindowState = Forms.FormWindowState.Maximized;
            else if (state == "hidden") Hide();
        }

        public ProbeReport Report() => new(Snapshot(), _normal ?? Snapshot(), _atClose, _closeCount);

        private ProbeState Snapshot()
        {
            NativeMethods.GetWindowRect(Handle, out var bounds);
            NativeMethods.GetClientRect(Handle, out var client);
            return new(NativeMethods.IsIconic(Handle), NativeMethods.IsWindowVisible(Handle), NativeMethods.GetParent(Handle).ToInt64(),
                bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, client.Right, client.Bottom,
                _lastSizeType, _lastSizeWidth, _lastSizeHeight);
        }

        protected override void OnFormClosing(Forms.FormClosingEventArgs args)
        {
            _atClose = Snapshot(); _closeCount++;
            if (CloseBehavior != "allow") args.Cancel = true;
            if (CloseBehavior == "tray" && !_atClose.IsMinimized && _atClose.Parent == 0) Hide();
            base.OnFormClosing(args);
        }

        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x5)
            {
                _lastSizeType = message.WParam.ToInt32();
                _lastSizeWidth = (int)(message.LParam.ToInt64() & 0xffff);
                _lastSizeHeight = (int)((message.LParam.ToInt64() >> 16) & 0xffff);
            }
            base.WndProc(ref message);
        }
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void NotifyWinEvent(uint eventType, nint handle, int objectId, int childId);
}
