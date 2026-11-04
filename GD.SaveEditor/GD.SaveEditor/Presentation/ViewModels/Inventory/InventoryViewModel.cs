using System.Globalization;
using System.Text;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Inventory;

/// <summary>
/// The inventory shown under the equipment, laid out like the game's: the main bag on the left with the iron bits
/// above it, the other bags on the right behind their tab buttons. The iron bits can be changed; the bags are
/// read-only for now.
/// </summary>
public sealed partial class InventoryViewModel : ViewModelBase
{
    private const int MainBagColumns = 12;
    private const int MainBagRows = 8;
    private const int BagColumns = 8;
    private const int BagRows = 8;

    /// <summary>Largest amount of iron bits accepted; the game keeps them in a signed 32-bit number.</summary>
    public const uint MaxMoney = int.MaxValue;

    private readonly IGameItemProvider _items;
    private readonly IGameIconProvider _icons;

    public InventoryViewModel(CharacterSave character, IGameItemProvider items, IGameIconProvider icons)
    {
        Character = character;
        _items = items;
        _icons = icons;

        var bags = character.Inventory?.Bags ?? [];
        MainBag = new InventoryBagViewModel(0, "Main bag", MainBagColumns, MainBagRows, bags.Count > 0 ? bags[0].Items : []);
        Bags = bags.Skip(1).Select((bag, i) => new InventoryBagViewModel(i + 1, $"Bag {i + 1}", BagColumns, BagRows, bag.Items)).ToList();
        SelectedBag = Bags.FirstOrDefault(b => b.Index == character.Inventory?.SelectedBag) ?? Bags.FirstOrDefault();
        MoneyText = FormatMoney(character.Money ?? 0);

        _ = LoadItemsAsync();
    }

    public CharacterSave Character { get; }

    /// <summary>The bags were read from the save file.</summary>
    public bool HasInventory => Character.Inventory is not null;

    public InventoryBagViewModel MainBag { get; }

    /// <summary>Bags beside the main bag, picked with the tab buttons.</summary>
    public IReadOnlyList<InventoryBagViewModel> Bags { get; }

    public bool HasBags => Bags.Count > 0;

    /// <summary>Bag shown on the right.</summary>
    [ObservableProperty]
    private InventoryBagViewModel? _selectedBag;

    /// <summary>Names and icons are being read from the game data.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Something was changed that is not written to the save file yet.</summary>
    public bool HasChanges => IsMoneyChanged;

    #region Iron bits

    /// <summary>The iron bits were read from the save file.</summary>
    public bool HasMoney => Character.Money is not null;

    /// <summary>The iron bits can be changed: the editor knows every part of the save file, so it can write it back.</summary>
    public bool CanEditMoney => HasMoney && Character.CanRewrite;

    public bool IsMoneyReadOnly => !CanEditMoney;

    /// <summary>Tooltip of the iron bits field.</summary>
    public string MoneyToolTip => CanEditMoney
        ? $"Iron bits. Type a new amount (0 to {FormatMoney(MaxMoney)}), then save the character."
        : $"Iron bits. They cannot be changed for this character: {Character.RewriteError ?? "they could not be read."}";

    /// <summary>Text of the iron bits field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoneyError), nameof(HasMoneyError), nameof(IsMoneyChanged), nameof(ChangedMoney), nameof(HasChanges))]
    private string _moneyText = string.Empty;

    /// <summary>Why <see cref="MoneyText"/> is not a valid amount; <c>null</c> when it is.</summary>
    public string? MoneyError => !CanEditMoney || TryParseMoney(MoneyText, out _)
        ? null
        : $"Enter a whole number from 0 to {FormatMoney(MaxMoney)}.";

    public bool HasMoneyError => MoneyError is not null;

    /// <summary>The field holds a valid amount that differs from the save file.</summary>
    public bool IsMoneyChanged => ChangedMoney is not null;

    /// <summary>The new amount of iron bits; <c>null</c> when it is unchanged or not valid.</summary>
    public uint? ChangedMoney =>
        CanEditMoney && TryParseMoney(MoneyText, out var money) && money != Character.Money ? money : null;

    /// <summary>Puts the amount of the save file back.</summary>
    [RelayCommand]
    private void ResetMoney() => MoneyText = FormatMoney(Character.Money ?? 0);

    /// <summary>Shows a valid amount with group separators ("85,458,538"), e.g. when the field loses focus.</summary>
    public void NormalizeMoneyText()
    {
        if (TryParseMoney(MoneyText, out var money))
            MoneyText = FormatMoney(money);
    }

    /// <summary>Digits with optional group separators of any culture ("85,458,538", "85 458 538", "85.458.538").</summary>
    public static bool TryParseMoney(string? text, out uint money)
    {
        money = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var digits = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            if (char.IsAsciiDigit(c))
                digits.Append(c);
            else if (c is not (',' or '.' or '\'' or '_') && !char.IsWhiteSpace(c))
                return false;
        }

        if (digits.Length is 0 or > 10
            || !ulong.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value > MaxMoney)
            return false;

        money = (uint)value;
        return true;
    }

    private static string FormatMoney(uint money) => money.ToString("N0", CultureInfo.CurrentCulture);

    #endregion

    /// <summary>Reads names and icons on a worker thread (database and archive access), then shows them.</summary>
    private async Task LoadItemsAsync()
    {
        var items = Bags.Prepend(MainBag).SelectMany(b => b.Items).Select(i => i.Item).ToList();
        if (items.Count == 0)
            return;

        IsLoading = true;
        try
        {
            var apply = await Task.Run(() => items.Select(i => i.Resolve(_items, _icons)).ToList());
            foreach (var action in apply)
                action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            // Names stay the record names; the bags still show what they hold.
        }
        finally
        {
            IsLoading = false;
        }
    }
}
