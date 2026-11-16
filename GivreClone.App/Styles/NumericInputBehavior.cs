using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GivreClone.App.Behaviors;

public sealed class NumericInputBehavior : AvaloniaObject
{
    public static readonly AttachedProperty<bool> SelectOnFocusProperty =
        AvaloniaProperty.RegisterAttached<NumericInputBehavior, TextBox, bool>("SelectOnFocus");

    static NumericInputBehavior()
    {
        SelectOnFocusProperty.Changed.AddClassHandler<TextBox>((text, change) =>
        {
            if (change.NewValue is true)
            {
                text.GotFocus += OnGotFocus;
                text.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            }
            else
            {
                text.GotFocus -= OnGotFocus;
                text.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            }
        });
    }

    public static bool GetSelectOnFocus(TextBox text) => text.GetValue(SelectOnFocusProperty);
    public static void SetSelectOnFocus(TextBox text, bool value) => text.SetValue(SelectOnFocusProperty, value);

    private static void OnGotFocus(object? sender, FocusChangedEventArgs e)
        => ((TextBox)sender!).SelectAll();

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var text = (TextBox)sender!;
        if (text.IsKeyboardFocusWithin || !e.GetCurrentPoint(text).Properties.IsLeftButtonPressed) return;
        text.Focus();
        text.SelectAll();
        // The first click focuses/selects; later clicks can position the caret normally.
        e.Handled = true;
    }
}
