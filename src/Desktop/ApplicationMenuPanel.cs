using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Vatowiec.Desktop;

// Keep StackPanel's directional menu navigation while moving the last group right.
public sealed class ApplicationMenuPanel : StackPanel
{
    public ApplicationMenuPanel() => Orientation = Orientation.Horizontal;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (Children.Count > 0)
        {
            var account = Children[^1];
            var bounds = account.Bounds;
            account.Arrange(new Rect(Math.Max(bounds.X, finalSize.Width - bounds.Width), bounds.Y, bounds.Width, bounds.Height));
        }
        return result;
    }
}
