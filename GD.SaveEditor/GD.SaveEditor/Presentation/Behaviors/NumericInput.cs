using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace GD.SaveEditor.Presentation.Behaviors;

/// <summary>
/// Attached behavior that turns a <see cref="TextBox"/> into an integer box:
/// only digits can be typed or pasted (and a leading minus sign when <see cref="MinimumProperty"/> is negative), and
/// Up/Down arrow keys change the value by one (by 100 with Shift).
/// </summary>
public sealed class NumericInput : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<NumericInput, TextBox, bool>("IsEnabled");

    /// <summary>Upper limit used by the Up arrow key.</summary>
    public static readonly AttachedProperty<int> MaximumProperty =
        AvaloniaProperty.RegisterAttached<NumericInput, TextBox, int>("Maximum", int.MaxValue);

    /// <summary>Lower limit used by the Down arrow key; a negative value allows a minus sign.</summary>
    public static readonly AttachedProperty<int> MinimumProperty =
        AvaloniaProperty.RegisterAttached<NumericInput, TextBox, int>("Minimum");

    static NumericInput()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(TextBox element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(TextBox element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static int GetMaximum(TextBox element) => element.GetValue(MaximumProperty);

    public static void SetMaximum(TextBox element, int value) => element.SetValue(MaximumProperty, value);

    public static int GetMinimum(TextBox element) => element.GetValue(MinimumProperty);

    public static void SetMinimum(TextBox element, int value) => element.SetValue(MinimumProperty, value);

    private static void OnIsEnabledChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
        {
            textBox.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
            textBox.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            textBox.AddHandler(TextBox.PastingFromClipboardEvent, OnPasting, RoutingStrategies.Bubble);
        }
        else
        {
            textBox.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
            textBox.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            textBox.RemoveHandler(TextBox.PastingFromClipboardEvent, OnPasting);
        }
    }

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextBox textBox || e.Text is not { } text)
            return;

        e.Handled = !(IsDigits(text) || (text == "-" && CanTypeMinus(textBox)));
    }

    /// <summary>A minus sign is allowed at the start of a box that takes negative values, once.</summary>
    private static bool CanTypeMinus(TextBox textBox)
    {
        var start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        var length = Math.Abs(textBox.SelectionEnd - textBox.SelectionStart);
        var text = textBox.Text ?? string.Empty;
        return GetMinimum(textBox) < 0 && start == 0 && !text.Remove(0, Math.Min(length, text.Length)).Contains('-');
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || e.Key is not (Key.Up or Key.Down))
            return;

        var current = int.TryParse(textBox.Text, out var value) ? value : 0;
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 100L : 1L;
        var next = (int)Math.Clamp(e.Key == Key.Up ? current + step : current - step, GetMinimum(textBox), GetMaximum(textBox));
        textBox.Text = next.ToString();
        textBox.CaretIndex = textBox.Text.Length;
        e.Handled = true;
    }

    /// <summary>Pasted text is checked after the clipboard was read; text that is not a number is not pasted.</summary>
    private static async void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || TopLevel.GetTopLevel(textBox)?.Clipboard is not { } clipboard)
            return;

        // The paste is done here (or not at all), with the text the clipboard has.
        e.Handled = true;
        try
        {
            var text = (await clipboard.TryGetTextAsync())?.Trim();
            if (string.IsNullOrEmpty(text)
                || !(IsDigits(text) || (text.StartsWith('-') && IsDigits(text[1..]) && CanTypeMinus(textBox))))
                return;

            var start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
            var end = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
            var current = textBox.Text ?? string.Empty;
            textBox.Text = current[..start] + text + current[Math.Min(end, current.Length)..];
            textBox.CaretIndex = start + text.Length;
        }
        catch (Exception)
        {
            // Async void: a clipboard that cannot be read pastes nothing.
        }
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
