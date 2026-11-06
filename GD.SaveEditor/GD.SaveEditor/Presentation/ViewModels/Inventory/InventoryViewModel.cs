using System.Globalization;
using System.Text;
using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Inventory;

/// <summary>
/// The inventory shown under the equipment, laid out like the game's: the main bag on the left with the iron bits
/// above it, the other bags on the right behind their tab buttons. The iron bits can be changed; items can be dragged
/// to another place in their bag or into another bag, made in the item builder, changed there, or removed (see
/// <see cref="ItemGridViewModel"/>).
/// </summary>
public sealed partial class InventoryViewModel : ItemGridViewModel
{
    private const int MainBagColumns = 12;
    private const int MainBagRows = 8;
    private const int BagColumns = 8;
    private const int BagRows = 8;

    /// <summary>Largest amount of iron bits accepted; the game keeps them in a signed 32-bit number.</summary>
    public const uint MaxMoney = int.MaxValue;

    public InventoryViewModel(CharacterSave character, IGameItemProvider items, IGameIconProvider icons, IDialogService dialogs)
        : base(items, icons, dialogs)
    {
        Character = character;

        var bags = character.Inventory?.Bags ?? [];
        MainBag = new InventoryBagViewModel(0, "Main bag", MainBagColumns, MainBagRows, bags.Count > 0 ? bags[0].Items : []);
        Bags = bags.Skip(1).Select((bag, i) => new InventoryBagViewModel(i + 1, $"Bag {i + 1}", BagColumns, BagRows, bag.Items)).ToList();
        Containers = Bags.Prepend(MainBag).ToList();
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

    /// <summary>The main bag, then the other bags, in the save file's order.</summary>
    public override IReadOnlyList<InventoryBagViewModel> Containers { get; }

    protected override InventoryBagViewModel DefaultContainer => MainBag;

    protected override string ContainerNoun => "bags";

    public override bool HasItemData => HasInventory;

    protected override string? ReadOnlyReason => Character.CanRewrite
        ? null
        : $"Items cannot be moved, added or changed for this character: {Character.RewriteError}";

    /// <summary>
    /// Items can be moved, added, changed and removed: the bags were read (the file has at least one) and the editor can
    /// write the save file back.
    /// </summary>
    public override bool CanEditItems => HasInventory && Character.CanRewrite && Character.Inventory?.Bags.Count > 0;

    /// <summary>Something was changed that is not written to the save file yet.</summary>
    public override bool HasChanges => IsMoneyChanged || AreItemsChanged;

    /// <summary>The contents of every bag to write into the save file; <c>null</c> when the bags are as in the file.</summary>
    public IReadOnlyList<IReadOnlyList<PlacedItem>>? ChangedBags => ChangedContainers;

    /// <summary>A bag beside the main bag is shown on the right once an item went there.</summary>
    public override void SelectContainer(InventoryBagViewModel container)
    {
        if (!ReferenceEquals(container, MainBag) && Bags.Contains(container))
            SelectedBag = container;
    }

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
}
