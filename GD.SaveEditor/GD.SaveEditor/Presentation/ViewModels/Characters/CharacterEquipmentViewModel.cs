using System.Globalization;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Characters;

/// <summary>An attribute or counter shown beside the equipment, e.g. "Physique 612".</summary>
public sealed record CharacterStatLine(string Label, string Value);

/// <summary>
/// "Character" tab: the equipment screen of the game (armor, jewelry, relic, both weapon sets) with the character's
/// attributes. Read-only for now.
/// </summary>
public sealed partial class CharacterEquipmentViewModel : ViewModelBase
{
    private readonly IGameItemProvider _items;
    private readonly IGameIconProvider _icons;

    public CharacterEquipmentViewModel(CharacterSave character, IGameItemProvider items, IGameIconProvider icons)
    {
        Character = character;
        _items = items;
        _icons = icons;

        var equipment = character.Equipment;
        EquipmentSlotViewModel Slot(EquipmentSlot slot, int set = 0) => new(slot, set, equipment?.Get(slot, set));

        Head = Slot(EquipmentSlot.Head);
        Amulet = Slot(EquipmentSlot.Amulet);
        Chest = Slot(EquipmentSlot.Chest);
        Legs = Slot(EquipmentSlot.Legs);
        Feet = Slot(EquipmentSlot.Feet);
        Hands = Slot(EquipmentSlot.Hands);
        Ring1 = Slot(EquipmentSlot.Ring1);
        Ring2 = Slot(EquipmentSlot.Ring2);
        Waist = Slot(EquipmentSlot.Waist);
        Shoulders = Slot(EquipmentSlot.Shoulders);
        Medal = Slot(EquipmentSlot.Medal);
        Relic = Slot(EquipmentSlot.Relic);
        MainHand1 = Slot(EquipmentSlot.MainHand, 1);
        OffHand1 = Slot(EquipmentSlot.OffHand, 1);
        MainHand2 = Slot(EquipmentSlot.MainHand, 2);
        OffHand2 = Slot(EquipmentSlot.OffHand, 2);

        ActiveWeaponSet = equipment?.ActiveWeaponSet ?? 1;
        ShownWeaponSet = ActiveWeaponSet;

        Stats = BuildStats(character.Stats);
        ErrorText = character.DetailsError is { } error ? $"The equipment could not be read from the save file: {error}" : null;
        ShowDatabaseHint = equipment is { Items.Count: > 0 } && !items.IsDatabaseAvailable;

        _ = LoadItemsAsync();
    }

    public CharacterSave Character { get; }

    public EquipmentSlotViewModel Head { get; }
    public EquipmentSlotViewModel Amulet { get; }
    public EquipmentSlotViewModel Chest { get; }
    public EquipmentSlotViewModel Legs { get; }
    public EquipmentSlotViewModel Feet { get; }
    public EquipmentSlotViewModel Hands { get; }
    public EquipmentSlotViewModel Ring1 { get; }
    public EquipmentSlotViewModel Ring2 { get; }
    public EquipmentSlotViewModel Waist { get; }
    public EquipmentSlotViewModel Shoulders { get; }
    public EquipmentSlotViewModel Medal { get; }
    public EquipmentSlotViewModel Relic { get; }
    public EquipmentSlotViewModel MainHand1 { get; }
    public EquipmentSlotViewModel OffHand1 { get; }
    public EquipmentSlotViewModel MainHand2 { get; }
    public EquipmentSlotViewModel OffHand2 { get; }

    /// <summary>Weapon set (1 or 2) the character holds in the game.</summary>
    public int ActiveWeaponSet { get; }

    /// <summary>Weapon set shown in the weapon slots; the swap button switches it like in the game.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MainHand), nameof(OffHand), nameof(WeaponSetText))]
    private int _shownWeaponSet;

    public EquipmentSlotViewModel MainHand => ShownWeaponSet == 2 ? MainHand2 : MainHand1;

    public EquipmentSlotViewModel OffHand => ShownWeaponSet == 2 ? OffHand2 : OffHand1;

    /// <summary>"Weapon set I (active)".</summary>
    public string WeaponSetText =>
        $"Weapon set {(ShownWeaponSet == 2 ? "II" : "I")}{(ShownWeaponSet == ActiveWeaponSet ? " (active)" : string.Empty)}";

    /// <summary>Attributes and unspent points; empty when they could not be read.</summary>
    public IReadOnlyList<CharacterStatLine> Stats { get; }

    public bool HasStats => Stats.Count > 0;

    /// <summary>Why the equipment is not shown; <c>null</c> when it was read.</summary>
    public string? ErrorText { get; }

    public bool HasError => ErrorText is not null;

    /// <summary>Items are shown by record name only: the game database has not been generated.</summary>
    public bool ShowDatabaseHint { get; }

    /// <summary>Names and icons are being read from the game data.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Nothing can be edited yet.</summary>
    public bool HasChanges => false;

    private IEnumerable<EquipmentSlotViewModel> AllSlots =>
    [
        Head, Amulet, Chest, Legs, Feet, Hands, Ring1, Ring2, Waist, Shoulders, Medal, Relic,
        MainHand1, OffHand1, MainHand2, OffHand2
    ];

    [RelayCommand]
    private void SwapWeaponSet() => ShownWeaponSet = ShownWeaponSet == 2 ? 1 : 2;

    /// <summary>Reads names and icons on a worker thread (database and archive access), then shows them.</summary>
    private async Task LoadItemsAsync()
    {
        var slots = AllSlots.Where(s => !s.IsEmpty).ToList();
        if (slots.Count == 0)
            return;

        IsLoading = true;
        try
        {
            var apply = await Task.Run(() => slots.Select(s => s.Resolve(_items, _icons)).ToList());
            foreach (var action in apply)
                action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            // Names stay the record names; the slots still show what is equipped.
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static IReadOnlyList<CharacterStatLine> BuildStats(CharacterStats? stats)
    {
        if (stats is null)
            return [];

        static string Number(double value) => value.ToString("N0", CultureInfo.CurrentCulture);

        var lines = new List<CharacterStatLine>
        {
            new("Physique", Number(stats.Physique)),
            new("Cunning", Number(stats.Cunning)),
            new("Spirit", Number(stats.Spirit)),
            new("Base health", Number(stats.Health)),
            new("Base energy", Number(stats.Energy)),
            new("Experience", Number(stats.Experience)),
            new("Devotion earned", Number(stats.TotalDevotion))
        };

        if (stats.AttributePoints > 0) lines.Add(new("Unspent attribute points", Number(stats.AttributePoints)));
        if (stats.SkillPoints > 0) lines.Add(new("Unspent skill points", Number(stats.SkillPoints)));
        if (stats.DevotionPoints > 0) lines.Add(new("Unspent devotion points", Number(stats.DevotionPoints)));
        return lines;
    }
}
