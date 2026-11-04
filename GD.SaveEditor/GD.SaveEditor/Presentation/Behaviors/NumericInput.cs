using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GD.SaveEditor.Presentation.Behaviors;

/// <summary>
/// Attached behavior that turns a <see cref="TextBox"/> into a non-negative integer box:
/// only digits can be typed or pasted, and Up/Down arrow keys change the value by one.
/// </summary>
public static class NumericInput
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NumericInput), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Upper limit used by the Up arrow key.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.RegisterAttached(
        "Maximum", typeof(int), typeof(NumericInput), new PropertyMetadata(int.MaxValue));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static int GetMaximum(DependencyObject element) => (int)element.GetValue(MaximumProperty);

    public static void SetMaximum(DependencyObject element, int value) => element.SetValue(MaximumProperty, value);

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

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !IsDigits(e.Text);

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
                var next = Math.Clamp(e.Key == Key.Up ? current + 1 : current - 1, 0, GetMaximum(textBox));
                textBox.Text = next.ToString();
                textBox.CaretIndex = textBox.Text.Length;
                e.Handled = true;
                break;
        }
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !IsDigits(text.Trim()))
            e.CancelCommand();
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
