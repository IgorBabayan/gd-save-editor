using System.Windows;
using System.Windows.Controls;

namespace GD.SaveEditor.Presentation.Controls;

/// <summary>
/// Hosts content that takes the height it is given but does not ask for any: its desired height is 0.
/// In a window that sizes itself to its content (<see cref="Window.SizeToContent"/>) the content then fills the
/// height the other parts of the window need, and a long list in it scrolls instead of making the window taller.
/// </summary>
public sealed class FillHeightDecorator : Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null)
            return default;

        Child.Measure(constraint);
        return new Size(Child.DesiredSize.Width, 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is null)
            return arrangeSize;

        // Measured above without the final height (possibly infinite): measure again with it, so scroll viewers
        // inside get the real viewport instead of being clipped.
        Child.Measure(arrangeSize);
        Child.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }
}
