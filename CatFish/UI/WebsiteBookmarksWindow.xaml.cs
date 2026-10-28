using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CatFish.Core;

namespace CatFish.UI;

public partial class WebsiteBookmarksWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly Action<Uri> _visit;
    private readonly ObservableCollection<WebsiteBookmark> _bookmarks;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _dirty;

    public WebsiteBookmarksWindow() : this(AppController.Settings, AppController.SettingsStore) { }

    internal WebsiteBookmarksWindow(AppSettings settings, SettingsStore store, Action<Uri>? visit = null)
    {
        _settings = settings; _store = store;
        _visit = visit ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        _bookmarks = new((settings.WebsiteBookmarks ?? new()).Where(item => item is not null && !item.IsEmpty).Select(item => item.Copy()));
        InitializeComponent();
        // Keep the common window frame while letting the table fill the remaining height.
        Content = null;
        Content = ViewFactory.Frame(this, Body, "网址收藏", compact: true);
        BookmarksGrid.ItemsSource = _bookmarks;
        foreach (var bookmark in _bookmarks) bookmark.PropertyChanged += BookmarkChanged;
        _bookmarks.CollectionChanged += BookmarksChanged;
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            try { await _store.SaveAsync(_settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = "保存失败：" + ex.Message; }
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _saveTimer.Stop();
            _bookmarks.CollectionChanged -= BookmarksChanged;
            foreach (var bookmark in _bookmarks) bookmark.PropertyChanged -= BookmarkChanged;
        };
    }

    private void BookmarkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WebsiteBookmark.IsEmpty)) RecordChanges();
    }
    private void BookmarksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (WebsiteBookmark item in e.OldItems) item.PropertyChanged -= BookmarkChanged;
        if (e.NewItems is not null) foreach (WebsiteBookmark item in e.NewItems) item.PropertyChanged += BookmarkChanged;
        RecordChanges();
    }

    private void RecordChanges()
    {
        _settings.WebsiteBookmarks = _bookmarks.Where(item => !item.IsEmpty).Select(item => item.Copy()).ToList();
        _dirty = true;
        StatusText.Text = "";
        _saveTimer.Stop(); _saveTimer.Start();
    }

    private bool CommitEdits() => BookmarksGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && BookmarksGrid.CommitEdit(DataGridEditingUnit.Row, true);

    private void Visit_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WebsiteBookmark bookmark || !CommitEdits()) return;
        if (!WebsiteBookmark.TryGetVisitUri(bookmark.Url, out var uri))
        {
            StatusText.Text = "请输入有效的 http 或 https 网址（例如 www.baidu.com）。";
            return;
        }
        try { _visit(uri!); StatusText.Text = ""; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        { StatusText.Text = "无法打开网址：" + ex.Message; }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WebsiteBookmark bookmark || !CommitEdits()) return;
        _bookmarks.Remove(bookmark);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!CommitEdits()) { e.Cancel = true; return; }
        _saveTimer.Stop();
        if (!_dirty) return;
        // Finish the current cell and the final write before the window or application exits.
        try { _store.SaveAsync(_settings).GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { e.Cancel = true; StatusText.Text = "保存失败，请重试：" + ex.Message; }
    }
}
