using System.Windows;

namespace GD.SaveEditor.Presentation.Views.Dialogs;

/// <summary>Themed Yes/No question. <see cref="Window.ShowDialog"/> returns <c>true</c> for Yes.</summary>
public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void OnYesClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnNoClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
