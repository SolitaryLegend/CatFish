using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using CatFish.Core;
using CatFish.UI;

internal static class MainWindowRegression
{
    public static void Run(Action<bool, string> check, Action<Window, string> render)
    {
        var main = new MainWindow();
        render(main, "main-bookmarks.png");
        var tools = main.ToolGrid.Children.OfType<Button>().ToArray();
        check(tools.Length == 6 && tools.All(button => button.ActualWidth < 158), "six launcher buttons are narrower than the previous layout");
        check(tools.Take(3).Select(Grid.GetRow).SequenceEqual(new[] { 0, 1, 2 }) && tools.Take(3).All(button => Grid.GetColumn(button) == 0)
            && tools.Skip(3).Select(Grid.GetRow).SequenceEqual(new[] { 0, 1, 2 }) && tools.Skip(3).All(button => Grid.GetColumn(button) == 2),
            "launcher preserves the first three tools on the left and the last three on the right");
        check(main.WebsiteBookmarksLink.Inlines.OfType<Run>().Single().Text == "网址收藏", "launcher exposes the website bookmarks link");

        var path = Path.Combine(AppContext.BaseDirectory, "bookmark-settings.json");
        var store = new SettingsStore();
        typeof(SettingsStore).GetField("_path", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(store, path);
        try
        {
            var defaults = new[] { ("电子书", "https://www.txt80.cc/"), ("线上桌游", "https://game.hullqin.cn/"), ("小霸王", "https://www.yikm.net/") };
            bool HasDefaultBookmarks(AppSettings settings) => settings.WebsiteBookmarks.Select(item => (item.Name, item.Url)).SequenceEqual(defaults);
            check(HasDefaultBookmarks(store.Load()), "first run offers all three default bookmarks in order");
            File.WriteAllText(path, "{}");
            check(HasDefaultBookmarks(store.Load()), "older settings without a bookmark field receive the defaults");
            File.WriteAllText(path, """{"WebsiteBookmarks":[]}""");
            var cleared = store.Load();
            check(HasDefaultBookmarks(cleared), "older empty bookmark lists receive the defaults once");
            cleared.WebsiteBookmarks.Clear();
            store.SaveAsync(cleared).GetAwaiter().GetResult();
            check(store.Load().WebsiteBookmarks.Count == 0, "clearing the initialized list does not re-add defaults");
            File.WriteAllText(path, """{"WebsiteBookmarks":[null,{}, {"Name":null,"Url":"www.baidu.com","Description":null}]}""");
            var settings = store.Load();
            check(settings.WebsiteBookmarks.Count == 1 && settings.WebsiteBookmarks[0].Name == "", "bookmark loading discards empty rows and tolerates null fields");
            settings.WebsiteBookmarks = new()
            {
                new() { Name = "百度", Url = "www.baidu.com", Description = "搜索" },
                new() { Name = "示例网站", Url = "https://example.com", Description = "资料与链接" }
            };
            Uri? visited = null;
            var window = new WebsiteBookmarksWindow(settings, store, uri => visited = uri);
            void ShowOffscreen(WebsiteBookmarksWindow preview)
            {
                preview.ShowActivated = false; preview.WindowStartupLocation = WindowStartupLocation.Manual;
                preview.Left = -5000; preview.Top = -5000; preview.Show(); preview.UpdateLayout();
                preview.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
            ShowOffscreen(window);
            render(window, "website-bookmarks.png");
            var grid = window.BookmarksGrid;
            check(grid.Columns.Count == 5 && grid.CanUserAddRows && grid.Items[^1] == CollectionView.NewItemPlaceholder,
                "bookmark table has five columns and one trailing dynamic blank row");

            // Use the actual DataGrid editing path, including its new-item placeholder.
            void Edit(object item, int column, string value, bool commit)
            {
                grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[column]);
                grid.ScrollIntoView(item, grid.Columns[column]); grid.UpdateLayout();
                check(grid.BeginEdit(), "table starts editing column " + column);
                var editor = Descendants<TextBox>(grid).Single();
                editor.Text = value; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                if (commit)
                    check(grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true), "table commits the edited row");
            }
            Edit(CollectionView.NewItemPlaceholder, 0, "新收藏", true);
            var added = grid.Items.OfType<WebsiteBookmark>().Last();
            check(grid.Items.OfType<WebsiteBookmark>().Count() == 3 && grid.Items[^1] == CollectionView.NewItemPlaceholder,
                "typing in the empty row creates a bookmark and another trailing empty row");
            Edit(added, 1, " example.com/path?q=中文 ", true);
            void WaitForAutosave()
            {
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(frame);
            }
            WaitForAutosave();
            check(store.Load().WebsiteBookmarks.Count == 3 && store.Load().WebsiteBookmarks[^1].Url == added.Url,
                "edits automatically save while the bookmark window remains open");
            Edit(added, 2, "待关闭时保存", false);
            window.Close();
            var reloaded = store.Load();
            check(reloaded.WebsiteBookmarks.Count == 3 && reloaded.WebsiteBookmarks[^1].Description == "待关闭时保存",
                "closing during editing commits and persists the final cell without saving the blank row");

            window = new WebsiteBookmarksWindow(reloaded, store, uri => visited = uri);
            ShowOffscreen(window);
            render(window, "website-bookmarks-added.png"); grid = window.BookmarksGrid;
            var first = grid.Items.OfType<WebsiteBookmark>().First();
            var visit = Descendants<Button>(grid).First(button => button.Content is TextBlock { Text: "访问" });
            visit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(visited?.AbsoluteUri == "https://www.baidu.com/", "visit action adds https and forwards the address to the browser launcher");
            first.Url = "file:///C:/Windows/notepad.exe"; visited = null;
            visit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(visited is null && window.StatusText.Text.Contains("有效"), "invalid website schemes show an error without launching");
            first.Url = "www.baidu.com";
            var delete = Descendants<Button>(grid).First(button => button.Content is TextBlock { Text: "删除" });
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(window.DeleteNotice.IsVisible && window.DeleteNoticeText.Text == "“百度”网址已删除，"
                && window.UndoDeleteLink.Inlines.OfType<Run>().Single().Text == "撤销",
                "deleting shows the bookmark name and an undo link in the footer");
            render(window, "website-bookmarks-deleted.png");
            WaitForAutosave();
            check(store.Load().WebsiteBookmarks.Count == 2 && store.Load().WebsiteBookmarks[0].Name == "示例网站",
                "deletion is saved while undo is still available");
            window.UndoDeleteLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            check(!window.DeleteNotice.IsVisible, "undo dismisses the footer notice");
            window.Close();
            var restored = store.Load().WebsiteBookmarks;
            check(restored.Count == 3 && restored[0] is { Name: "百度", Url: "www.baidu.com", Description: "搜索" },
                "undo restores the original row, including its position, and saves it");

            var empty = new WebsiteBookmarksWindow(new AppSettings { WebsiteBookmarks = [] }, store);
            ShowOffscreen(empty);
            render(empty, "website-bookmarks-empty.png");
            check(empty.BookmarksGrid.Items.Count == 1 && empty.BookmarksGrid.Items[0] == CollectionView.NewItemPlaceholder
                && Descendants<Button>(empty.BookmarksGrid).Where(button => button.Content is TextBlock).All(button => button.Visibility == Visibility.Collapsed),
                "an empty list still shows one input row with no visit or delete action");
            grid = empty.BookmarksGrid;
            Edit(CollectionView.NewItemPlaceholder, 0, "取消新增", false);
            grid.CancelEdit(DataGridEditingUnit.Cell); grid.CancelEdit(DataGridEditingUnit.Row);
            check(grid.Items.Count == 1 && grid.Items[0] == CollectionView.NewItemPlaceholder,
                "cancelling a new row restores the single empty input row");
            empty.Close();

            foreach (var invalid in new[] { "", "   ", "ftp://example.com", "javascript:alert(1)", "not a website", "https://" })
                check(!WebsiteBookmark.TryGetVisitUri(invalid, out _), "reject invalid address: " + invalid);
            check(WebsiteBookmark.TryGetVisitUri("http://localhost:8080/path", out var local) && local!.Port == 8080,
                "explicit http addresses and custom ports are preserved");

            var cancellable = new WebsiteBookmark { Name = "原名称", Url = "example.com" };
            cancellable.BeginEdit(); cancellable.Name = "临时名称"; cancellable.CancelEdit();
            check(cancellable.Name == "原名称", "cancelling row editing restores the previous values");

            // Exercise the real launcher's click handler and singleton feature management.
            typeof(AppController).GetField("_main", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, main);
            main.WebsiteBookmarksLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            var opened = Application.Current.Windows.OfType<WebsiteBookmarksWindow>().Single();
            main.WebsiteBookmarksLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            check(opened.IsVisible && Application.Current.Windows.OfType<WebsiteBookmarksWindow>().Count() == 1,
                "clicking the launcher link opens and reuses one bookmark window");
            opened.Close();
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
