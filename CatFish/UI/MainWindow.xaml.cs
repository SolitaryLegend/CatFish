using System.Windows.Interop;
using System.Windows;
using CatFish.Core;

namespace CatFish.UI;

public partial class MainWindow : Window
{
 internal bool ExitRequested { get; set; }
 public MainWindow()
 {
  InitializeComponent(); SourceInitialized += (_, _) => AppController.Initialize(this);
  ViewFactory.EnableEscapeClose(this);
  Loaded += (_, _) => CenterOnScreen();
  StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && !ExitRequested) HideToTray(); };
  Closing += (_, e) =>
  {
   if (!ExitRequested) { e.Cancel = true; HideToTray(); return; }
   AppController.Shutdown();
  };
 }
 public void HideToTray()
 {
  System.Windows.Input.FocusManager.SetFocusedElement(this, null); System.Windows.Input.Keyboard.ClearFocus(); Hide();
 }
 public void RestoreFromTray()
 {
  WindowState = WindowState.Normal; Show();
  AppController.WindowAppearance.Restore(this);
  Opacity = 1; CenterOnScreen(); Activate();
  NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
 }
 public void CenterOnScreen()
 {
  var area = SystemParameters.WorkArea;
  Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
  Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
 }
 private void OpenTransparency_Click(object sender, RoutedEventArgs e) => AppController.OpenTool("transparency");
 private void OpenSwitcher_Click(object sender, RoutedEventArgs e) => AppController.OpenTool("switcher");
 private void OpenSimulation_Click(object sender, RoutedEventArgs e) => AppController.OpenTool("simulation");
 private void OpenPopup_Click(object sender, RoutedEventArgs e) => AppController.OpenTool("popup");
 private void OpenTxtReader_Click(object sender, RoutedEventArgs e) => AppController.OpenTool("txt-reader");
 private void OpenMusic_Click(object sender, RoutedEventArgs e) => AppController.OpenTool("music");
 private void OpenWebsiteBookmarks_Click(object sender, RoutedEventArgs e) => AppController.ShowFeature("website-bookmarks", () => new WebsiteBookmarksWindow());
 private void OpenHotkeys_Click(object sender, RoutedEventArgs e) => AppController.ShowFeature("hotkeys", () => new HotkeySettingsWindow());
 private void Exit_Click(object sender, RoutedEventArgs e) => AppController.RequestExit();
 private void CloseToTray_Click(object sender, RoutedEventArgs e) => Close();
 private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
 {
  if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
 }
}
