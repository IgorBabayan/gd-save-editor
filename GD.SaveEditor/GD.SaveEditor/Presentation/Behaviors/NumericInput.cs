using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GD.SaveEditor.Presentation.Behaviors;

/// <summary>
/// Attached behavior that turns a <see cref="TextBox"/> into an integer box:
/// only digits can be typed or pasted (and a leading minus sign when <see cref="MinimumProperty"/> is negative), and
/// Up/Down arrow keys change the value by one (by 100 with Shift).
/// </summary>
public static class NumericInput
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NumericInput), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Upper limit used by the Up arrow key.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.RegisterAttached(
        "Maximum", typeof(int), typeof(NumericInput), new PropertyMetadata(int.MaxValue));

    /// <summary>Lower limit used by the Down arrow key; a negative value allows a minus sign.</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.RegisterAttached(
        "Minimum", typeof(int), typeof(NumericInput), new PropertyMetadata(0));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static int GetMaximum(DependencyObject element) => (int)element.GetValue(MaximumProperty);

    public static void SetMaximum(DependencyObject element, int value) => element.SetValue(MaximumProperty, value);

    public static int GetMinimum(DependencyObject element) => (int)element.GetValue(MinimumProperty);

    public static void SetMinimum(DependencyObject element, int value) => element.SetValue(MinimumProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
            return;

        if ((bool)e.NewValue)
        {
            textBox.PreviewTextInput += OnPreviewTextInput;
            textBox.PreviewKeyDown += OnPreviewKeyDown;
            DataObject.AddPastingHandler(textBox, OnPasting);
            InputMethod.SetIsInputMethodEnabled(textBox, false);
        }
        else
        {
            textBox.PreviewTextInput -= OnPreviewTextInput;
            textBox.PreviewKeyDown -= OnPreviewKeyDown;
            DataObject.RemovePastingHandler(textBox, OnPasting);
            textBox.ClearValue(InputMethod.IsInputMethodEnabledProperty);
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = (TextBox)sender;
        e.Handled = !(IsDigits(e.Text) || (e.Text == "-" && CanTypeMinus(textBox)));
    }

    /// <summary>A minus sign is allowed at the start of a box that takes negative values, once.</summary>
    private static bool CanTypeMinus(TextBox textBox) =>
        GetMinimum(textBox) < 0
        && textBox.SelectionStart == 0
        && !textBox.Text.Remove(0, textBox.SelectionLength).Contains('-');

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var textBox = (TextBox)sender;

        switch (e.Key)
        {
            // Space does not raise PreviewTextInput.
            case Key.Space:
                e.Handled = true;
                break;

            case Key.Up or Key.Down:
                var current = int.TryParse(textBox.Text, out var value) ? value : 0;
                var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 100L : 1L;
                var next = (int)Math.Clamp(e.Key == Key.Up ? current + step : current - step, GetMinimum(textBox), GetMaximum(textBox));
                textBox.Text = next.ToString();
                textBox.CaretIndex = textBox.Text.Length;
                e.Handled = true;
                break;
        }
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text)
        {
            e.CancelCommand();
            return;
        }

        var trimmed = text.Trim();
        var textBox = (TextBox)sender;
        var valid = IsDigits(trimmed)
                    || (trimmed.StartsWith('-') && IsDigits(trimmed[1..]) && CanTypeMinus(textBox));
        if (!valid)
            e.CancelCommand();
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
