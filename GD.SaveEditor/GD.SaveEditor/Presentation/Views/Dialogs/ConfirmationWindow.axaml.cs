using Avalonia.Controls;
using GD.SaveEditor.Presentation.Services;
using Avalonia.Interactivity;

namespace GD.SaveEditor.Presentation.Views.Dialogs;

/// <summary>Themed Yes/No question. It closes with <c>true</c> for Yes, <c>false</c> for No.</summary>
public partial class ConfirmationWindow : Window
{
    /// <summary>For the XAML previewer.</summary>
    public ConfirmationWindow() : this("Confirm", string.Empty)
    {
    }

    public ConfirmationWindow(string title, string message)
    {
        InitializeComponent();
        WindowHelper.UseCustomChrome(this);
        Title = title;
        MessageText.Text = message;
        // The default button takes the keyboard, so Enter answers Yes and Esc answers No right away.
        Opened += (_, _) => YesButton.Focus();
    }

    private void OnYesClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnNoClick(object? sender, RoutedEventArgs e) => Close(false);
}
