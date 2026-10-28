using System.Windows;
using System.Windows.Controls;

namespace CatFish.Features.Reader;

internal static class ReaderInputWheel
{
    public static void Attach(UIElement target, Action<int> adjust, Func<bool>? enabled = null)
    {
        var remainder = 0;
        target.PreviewMouseWheel += (_, e) =>
        {
            if (enabled is not null && !enabled()) return;
            e.Handled = true; remainder += e.Delta;
            var notches = remainder / 120; remainder %= 120;
            if (notches != 0) adjust(notches);
        };
        target.MouseLeave += (_, _) => remainder = 0;
    }

    public static void Attach(ComboBox combo) => Attach(combo, notches =>
    {
        if (combo.Items.Count == 0) return;
        combo.SelectedIndex = combo.SelectedIndex < 0 ? 0 : Math.Clamp(combo.SelectedIndex - notches, 0, combo.Items.Count - 1);
    }, () => !combo.IsDropDownOpen);
}
