using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CatFish.Features.Popup;

// Each view owns its clock; immutable decoded frames are shared between views.
internal sealed class PopupBackgroundPlayer : IDisposable
{
    private readonly Image _image;
    private readonly DispatcherTimer _timer;
    private PopupAnimation? _animation;
    private int _frame;
    public event Action? FrameChanged;

    public PopupBackgroundPlayer(Image image)
    {
        _image = image;
        _timer = new DispatcherTimer(DispatcherPriority.Render, image.Dispatcher);
        _timer.Tick += Advance;
        image.Loaded += Loaded;
        image.Unloaded += Unloaded;
        image.IsVisibleChanged += VisibilityChanged;
    }

    public void SetBackground(string id)
    {
        _timer.Stop();
        _animation = PopupBackgrounds.LoadAnimation(id);
        _frame = 0;
        ShowFrame();
        UpdatePlayback();
    }

    private void Advance(object? sender, EventArgs args)
    {
        _frame = (_frame + 1) % _animation!.Frames.Count;
        ShowFrame();
    }

    private void ShowFrame()
    {
        _image.Source = _animation!.Frames[_frame];
        _timer.Interval = _animation.Delays[_frame];
        FrameChanged?.Invoke();
    }

    private void Loaded(object sender, RoutedEventArgs args) => UpdatePlayback();
    private void Unloaded(object sender, RoutedEventArgs args) => _timer.Stop();
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => UpdatePlayback();
    private void UpdatePlayback()
    {
        if (_image.IsLoaded && _image.IsVisible && _animation?.Frames.Count > 1) _timer.Start();
        else _timer.Stop();
    }

    public void Dispose()
    {
        _timer.Stop(); _timer.Tick -= Advance;
        _image.Loaded -= Loaded; _image.Unloaded -= Unloaded; _image.IsVisibleChanged -= VisibilityChanged;
        FrameChanged = null; _animation = null;
    }
}
