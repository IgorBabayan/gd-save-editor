using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GD.SaveEditor.Presentation.ViewModels.Inventory;

namespace GD.SaveEditor.Presentation.Views.Inventory;

/// <summary>
/// Bags and iron bits of the selected character, shown under the equipment in the "Character" tab. Moving, adding,
/// changing and removing items is done by <see cref="ItemGridView"/>.
/// </summary>
public partial class InventoryView : ItemGridView
{
    public InventoryView()
    {
        InitializeComponent();
    }

    private InventoryViewModel? ViewModel => DataContext as InventoryViewModel;

    #region Iron bits

    /// <summary>Shows the typed amount with group separators once the field is left.</summary>
    private void OnMoneyLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.NormalizeMoneyText();

    /// <summary>Enter formats the amount, Esc puts back the amount of the save file.</summary>
    private void OnMoneyKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not TextBox textBox)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                viewModel.NormalizeMoneyText();
                textBox.CaretIndex = textBox.Text?.Length ?? 0;
                e.Handled = true;
                break;
            case Key.Escape when viewModel.IsMoneyChanged:
                viewModel.ResetMoneyCommand.Execute(null);
                textBox.CaretIndex = textBox.Text?.Length ?? 0;
                e.Handled = true;
                break;
        }
    }

    #endregion
}
