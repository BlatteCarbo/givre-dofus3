using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace GivreClone.App.Views;

public partial class BotsView
{
    private void OnCompactFieldGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Control field) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(field)?.FocusManager?.GetFocusedElement() is Control focused
                && (ReferenceEquals(focused, field) || focused.GetVisualAncestors().Contains(field)))
                RevealFocusedField(focused);
        }, DispatcherPriority.Loaded);
        RevealFocusedField(field);
    }

    private static void RevealFocusedField(Control field)
    {
        foreach (var scroll in field.GetVisualAncestors().OfType<ScrollViewer>())
        {
            if (field.TranslatePoint(default, scroll) is not { } point) continue;
            double dx = scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled
                && scroll.Extent.Width > scroll.Viewport.Width
                ? point.X < 0 ? point.X : Math.Max(0, point.X + field.Bounds.Width - scroll.Viewport.Width)
                : 0;
            double dy = scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                && scroll.Extent.Height > scroll.Viewport.Height
                ? point.Y < 0 ? point.Y : Math.Max(0, point.Y + field.Bounds.Height - scroll.Viewport.Height)
                : 0;
            scroll.Offset = new Vector(scroll.Offset.X + dx, scroll.Offset.Y + dy);
            scroll.UpdateLayout();
        }
    }
}
