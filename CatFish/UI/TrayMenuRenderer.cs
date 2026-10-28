using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using Size = System.Drawing.Size;

namespace CatFish.UI;

internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer, IDisposable
{
    private readonly Color _background = ThemeColor("CanvasBrush");
    private readonly Color _border = ThemeColor("BorderBrushSoft");
    private readonly Color _text = ThemeColor("TextBrush");
    private readonly Color _muted = ThemeColor("MutedBrush");
    private readonly Color _accent = ThemeColor("AccentBrush");
    private readonly Font _font = new("Microsoft YaHei UI", 9f);
    private readonly SolidBrush _hoverBrush;
    private readonly Pen _borderPen;
    private readonly Dictionary<Size, GraphicsPath> _highlights = new();
    private readonly Forms.Timer _hoverTimer = new() { Interval = 90 };
    private Forms.ToolStripMenuItem? _pending;

    public TrayMenuRenderer()
    {
        RoundedEdges = false;
        _hoverBrush = new SolidBrush(Color.FromArgb(
            (_background.R * 9 + _accent.R) / 10, (_background.G * 9 + _accent.G) / 10, (_background.B * 9 + _accent.B) / 10));
        _borderPen = new Pen(_border);
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop(); var item = _pending; _pending = null;
            if (item is not { IsDisposed: false, Selected: true, Owner: { Visible: true } owner }) return;
            foreach (var sibling in owner.Items.OfType<Forms.ToolStripMenuItem>())
                if (!ReferenceEquals(sibling, item) && sibling.HasDropDownItems && sibling.DropDown.Visible) sibling.HideDropDown();
            if (item.HasDropDownItems) item.ShowDropDown();
        };
    }

    private static Color ThemeColor(string key)
    {
        var color = ((SolidColorBrush)Application.Current.Resources[key]).Color;
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public void Apply(Forms.ToolStripDropDown menu)
    {
        menu.Renderer = this; menu.BackColor = _background; menu.ForeColor = _text;
        menu.Font = _font; menu.Padding = new Forms.Padding(3, 4, 3, 4);
        menu.Closed += (_, _) => { _hoverTimer.Stop(); _pending = null; };
        void RoundMenu()
        {
            if (menu.Width < 12 || menu.Height < 12) return;
            using var outline = MenuOutline(menu.Size, menu.DeviceDpi / 96f, 0);
            menu.Region = new Region(outline);
        }
        menu.SizeChanged += (_, _) => RoundMenu(); RoundMenu();
        if (menu is Forms.ToolStripDropDownMenu dropdown) { dropdown.ShowImageMargin = false; dropdown.ShowCheckMargin = false; }
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.Margin = Forms.Padding.Empty;
            item.Padding = new Forms.Padding(5, 1, 5, 1);
            if (item is Forms.ToolStripSeparator separator)
            {
                separator.AutoSize = false; separator.Height = 7;
                separator.Padding = Forms.Padding.Empty;
            }
            if (item is not Forms.ToolStripMenuItem entry) continue;
            entry.MouseEnter += (_, _) =>
            {
                _hoverTimer.Stop(); _pending = entry;
                if (entry.Owner?.Visible == true) _hoverTimer.Start();
            };
            entry.MouseLeave += (_, _) => { if (ReferenceEquals(_pending, entry)) { _hoverTimer.Stop(); _pending = null; } };
            if (entry.HasDropDownItems)
            {
                var original = entry.DropDown;
                var children = original.Items.Cast<Forms.ToolStripItem>().ToArray();
                original.Items.Clear();
                entry.DropDown = new TrayDropDownMenu();
                entry.DropDown.Items.AddRange(children); original.Dispose();
                Apply(entry.DropDown);
            }
        }
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) => e.Graphics.Clear(_background);
    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var outline = MenuOutline(e.ToolStrip.Size, e.ToolStrip.DeviceDpi / 96f, .5f);
        var smoothing = e.Graphics.SmoothingMode; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(_borderPen, outline); e.Graphics.SmoothingMode = smoothing;
    }
    private static GraphicsPath MenuOutline(Size size, float scale, float inset)
    {
        var bounds = new RectangleF(inset, inset, Math.Max(1, size.Width - 1 - inset * 2), Math.Max(1, size.Height - 1 - inset * 2));
        var diameter = Math.Min(8 * scale, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
        return path;
    }
    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed)) return;
        var size = e.Item.Size;
        if (!_highlights.TryGetValue(size, out var path))
        {
            if (_highlights.Count > 32) { foreach (var cached in _highlights.Values) cached.Dispose(); _highlights.Clear(); }
            var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            var bounds = new RectangleF(2, 1, Math.Max(1, size.Width - 4), Math.Max(1, size.Height - 2));
            var diameter = Math.Min(8 * scale, bounds.Height);
            path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
            _highlights[size] = path;
        }
        var smoothing = e.Graphics.SmoothingMode; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(_hoverBrush, path);
        e.Graphics.SmoothingMode = smoothing;
    }
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? _muted : e.Item.Selected || e.Item.Pressed ? _accent : _text;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item is { Enabled: false } ? _muted : e.Item is { Selected: true } or { Pressed: true } ? _accent : _muted;
        base.OnRenderArrow(e);
    }
    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        e.Graphics.DrawLine(_borderPen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
    }

    public void Dispose()
    {
        _hoverTimer.Dispose(); _font.Dispose(); _hoverBrush.Dispose(); _borderPen.Dispose();
        foreach (var path in _highlights.Values) path.Dispose(); _highlights.Clear();
    }
}
