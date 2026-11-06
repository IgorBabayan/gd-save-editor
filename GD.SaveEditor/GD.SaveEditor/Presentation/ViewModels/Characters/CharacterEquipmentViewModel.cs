using System.Globalization;
using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.ViewModels.Inventory;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Items;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Presentation.ViewModels.Characters;

/// <summary>An attribute or counter shown beside the equipment, e.g. "Physique 612".</summary>
public sealed record CharacterStatLine(string Label, string Value);

/// <summary>
/// "Character" tab: the equipment screen of the game (armor, jewelry, relic, both weapon sets) with the character's
/// attributes. A slot's item can be changed or taken off, and an empty slot can get a new item, in the item inspector
/// (the compact item builder) like a bag item.
/// </summary>
public sealed partial class CharacterEquipmentViewModel : ViewModelBase
{
    private readonly IGameItemProvider _items;
    private readonly IGameIconProvider _icons;
    private readonly IDialogService _dialogs;

    /// <summary>The 16 slots in the file's order: armor and jewelry, then weapon set 1 and 2.</summary>
    private readonly List<SlotState> _slots = [];

    public CharacterEquipmentViewModel(CharacterSave character, IGameItemProvider items, IGameIconProvider icons, IDialogService dialogs)
    {
        Character = character;
        _items = items;
        _icons = icons;
        _dialogs = dialogs;

        var equipment = character.Equipment;
        for (var i = 0; i <= (int)EquipmentSlot.Relic; i++)
            _slots.Add(new SlotState((EquipmentSlot)i, 0, equipment?.Get((EquipmentSlot)i)));
        foreach (var set in new[] { 1, 2 })
        {
            _slots.Add(new SlotState(EquipmentSlot.MainHand, set, equipment?.Get(EquipmentSlot.MainHand, set)));
            _slots.Add(new SlotState(EquipmentSlot.OffHand, set, equipment?.Get(EquipmentSlot.OffHand, set)));
        }

        ActiveWeaponSet = equipment?.ActiveWeaponSet ?? 1;
        ShownWeaponSet = ActiveWeaponSet;

        Stats = BuildStats(character.Stats);
        ErrorText = character.DetailsError is { } error ? $"The equipment could not be read from the save file: {error}" : null;
        ShowDatabaseHint = equipment is { Items.Count: > 0 } && !items.IsDatabaseAvailable;

        _ = LoadItemsAsync();
    }

    public CharacterSave Character { get; }

    public EquipmentSlotViewModel Head => View(EquipmentSlot.Head);
    public EquipmentSlotViewModel Amulet => View(EquipmentSlot.Amulet);
    public EquipmentSlotViewModel Chest => View(EquipmentSlot.Chest);
    public EquipmentSlotViewModel Legs => View(EquipmentSlot.Legs);
    public EquipmentSlotViewModel Feet => View(EquipmentSlot.Feet);
    public EquipmentSlotViewModel Hands => View(EquipmentSlot.Hands);
    public EquipmentSlotViewModel Ring1 => View(EquipmentSlot.Ring1);
    public EquipmentSlotViewModel Ring2 => View(EquipmentSlot.Ring2);
    public EquipmentSlotViewModel Waist => View(EquipmentSlot.Waist);
    public EquipmentSlotViewModel Shoulders => View(EquipmentSlot.Shoulders);
    public EquipmentSlotViewModel Medal => View(EquipmentSlot.Medal);
    public EquipmentSlotViewModel Relic => View(EquipmentSlot.Relic);
    public EquipmentSlotViewModel MainHand1 => View(EquipmentSlot.MainHand, 1);
    public EquipmentSlotViewModel OffHand1 => View(EquipmentSlot.OffHand, 1);
    public EquipmentSlotViewModel MainHand2 => View(EquipmentSlot.MainHand, 2);
    public EquipmentSlotViewModel OffHand2 => View(EquipmentSlot.OffHand, 2);

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

    /// <summary>A slot holds another item than in the save file (changed, new, or taken off).</summary>
    public bool HasChanges => _slots.Any(s => s.IsChanged);

    private IEnumerable<EquipmentSlotViewModel> AllSlots => _slots.Select(s => s.View);

    [RelayCommand]
    private void SwapWeaponSet() => ShownWeaponSet = ShownWeaponSet == 2 ? 1 : 2;

    #region Changing the equipment

    /// <summary>
    /// Items can be equipped, changed and taken off: the equipment was read and the editor can write the save file
    /// back.
    /// </summary>
    public bool CanEditItems => Character.CanRewrite && Character.Equipment is not null && Character.Inventory is { Bags.Count: > 0 };

    /// <summary>Why the equipment cannot be changed; <c>null</c> when it can (or when it was not read at all).</summary>
    public string? EditHint => CanEditItems || Character.Equipment is null
        ? null
        : !Character.CanRewrite
            ? $"The equipment cannot be changed for this character: {Character.RewriteError}"
            : "The equipment cannot be changed for this character: the save file has no items.";

    public bool HasEditHint => EditHint is not null;

    /// <summary>What the last change could not do (an item that does not fit the slot); <c>null</c> when it worked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => StatusMessage is not null;

    /// <summary>The slots that hold another item than in the save file, to write them; <c>null</c> when none does.</summary>
    public IReadOnlyList<EquipmentChange>? ChangedEquipment =>
        HasChanges ? _slots.Where(s => s.IsChanged).Select(s => new EquipmentChange(s.Slot, s.WeaponSet, s.Current)).ToList() : null;

    /// <summary>
    /// Opens the item inspector next to <paramref name="anchor"/> (the slot) on the item of <paramref name="slot"/>, to
    /// change or take it off; on an empty slot, to build a new item of <paramref name="kind"/> (the slot's usual kind
    /// when none is given) for it.
    /// </summary>
    public void EditSlot(EquipmentSlotViewModel slot, object? anchor = null, NewItemKind? kind = null)
    {
        if (!CanEditItems || Find(slot) is not { } state)
            return;

        StatusMessage = null;
        if (state.Current is null)
            _dialogs.ShowItemInspector(anchor, null, built => Equip(state, built), kind: kind ?? EquipmentSlots.GetKind(state.Slot));
        else
            _dialogs.ShowItemInspector(anchor, state.Current, built => Equip(state, built), () => SetItem(state, null));
    }

    /// <summary>Takes the item off <paramref name="slot"/> after asking.</summary>
    public void RemoveItem(EquipmentSlotViewModel slot)
    {
        if (!CanEditItems || Find(slot) is not { Current: not null } state)
            return;

        if (!_dialogs.Confirm("Remove item", $"Remove {slot.Name} from the character? It is deleted from the save file when you save the character."))
            return;

        StatusMessage = null;
        SetItem(state, null);
    }

    /// <summary>Puts every slot back as the save file has it.</summary>
    [RelayCommand]
    private void ResetEquipment()
    {
        // An open inspector would edit an item that is about to be put back.
        _dialogs.CloseItemInspector();
        foreach (var state in _slots.Where(s => s.IsChanged))
        {
            state.Revert();
            OnSlotChanged(state);
        }

        StatusMessage = null;
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>Puts <paramref name="built"/> in the slot when the slot takes it, like the game.</summary>
    private void Equip(SlotState state, BuiltItem built)
    {
        var info = _items.GetItem(built.Item.BaseRecord);
        var cls = info?.Class;
        var name = info?.Name ?? System.IO.Path.GetFileNameWithoutExtension(built.Item.BaseRecord);

        // Without the class (record unknown to the database) the slot cannot be checked: the item is put in as it is.
        if (cls is not null && !EquipmentSlots.Accepts(state.Slot, cls))
        {
            StatusMessage = $"{name} ({ItemClasses.GetLabel(cls) ?? cls}) cannot be worn in the {state.View.Label.ToLowerInvariant()} slot, so it was not equipped.";
            return;
        }

        if (state.WeaponSet > 0)
        {
            var other = Find(state.Slot == EquipmentSlot.MainHand ? EquipmentSlot.OffHand : EquipmentSlot.MainHand, state.WeaponSet);
            var otherClass = other?.Current is { } otherItem ? _items.GetItem(otherItem.BaseRecord)?.Class : null;
            if (state.Slot == EquipmentSlot.MainHand && EquipmentSlots.IsTwoHanded(cls) && other?.Current is not null)
            {
                StatusMessage = $"{name} is a two-handed weapon: take the off-hand item off first.";
                return;
            }

            if (state.Slot == EquipmentSlot.OffHand && EquipmentSlots.IsTwoHanded(otherClass))
            {
                StatusMessage = "The main hand holds a two-handed weapon: nothing can be held in the off hand with it.";
                return;
            }
        }

        // Gear does not stack: a slot always holds one item.
        SetItem(state, built.Item.WithQuantity(1));
    }

    private void SetItem(SlotState state, SaveItem? item)
    {
        var view = state.Set(item);
        OnSlotChanged(state);
        OnPropertyChanged(nameof(HasChanges));
        if (!view.IsEmpty)
            _ = ResolveAsync(view);
    }

    /// <summary>The slot's property shows its new view (and the shown weapon slots, for a weapon).</summary>
    private void OnSlotChanged(SlotState state)
    {
        OnPropertyChanged(state.PropertyName);
        if (state.WeaponSet == ShownWeaponSet)
            OnPropertyChanged(state.Slot == EquipmentSlot.MainHand ? nameof(MainHand) : nameof(OffHand));
    }

    /// <summary>Reads the name, icon and tooltip of a new or changed item on a worker thread.</summary>
    private async Task ResolveAsync(EquipmentSlotViewModel view)
    {
        try
        {
            var apply = await Task.Run(() => view.Resolve(_items, _icons));
            apply();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            // The item keeps its record name; it is still written.
        }
    }

    private EquipmentSlotViewModel View(EquipmentSlot slot, int weaponSet = 0) => Find(slot, weaponSet)!.View;

    private SlotState? Find(EquipmentSlot slot, int weaponSet) =>
        _slots.FirstOrDefault(s => s.Slot == slot && s.WeaponSet == weaponSet);

    private SlotState? Find(EquipmentSlotViewModel view) => _slots.FirstOrDefault(s => ReferenceEquals(s.View, view));

    /// <summary>One slot: the item of the save file and the one it holds now, each with its view.</summary>
    private sealed class SlotState
    {
        private readonly EquipmentSlotViewModel _originalView;

        public SlotState(EquipmentSlot slot, int weaponSet, SaveItem? original)
        {
            Slot = slot;
            WeaponSet = weaponSet;
            Original = original is { IsEmpty: false } ? original : null;
            Current = Original;
            _originalView = new EquipmentSlotViewModel(slot, weaponSet, Original);
            View = _originalView;
        }

        public EquipmentSlot Slot { get; }

        public int WeaponSet { get; }

        public SaveItem? Original { get; }

        /// <summary>The item to write; <c>null</c> for an empty slot.</summary>
        public SaveItem? Current { get; private set; }

        public EquipmentSlotViewModel View { get; private set; }

        public bool IsChanged => !ReferenceEquals(Current, Original);

        /// <summary>"Head", "MainHand2": the view model property that shows the slot.</summary>
        public string PropertyName => Slot switch
        {
            EquipmentSlot.MainHand => WeaponSet == 2 ? nameof(MainHand2) : nameof(MainHand1),
            EquipmentSlot.OffHand => WeaponSet == 2 ? nameof(OffHand2) : nameof(OffHand1),
            _ => Slot.ToString()
        };

        /// <summary>Puts <paramref name="item"/> in the slot; returns its view, to be resolved.</summary>
        public EquipmentSlotViewModel Set(SaveItem? item)
        {
            Current = item;
            View = new EquipmentSlotViewModel(Slot, WeaponSet, item);
            return View;
        }

        /// <summary>Puts back the item of the save file with its view as read.</summary>
        public void Revert()
        {
            Current = Original;
            View = _originalView;
        }
    }

    #endregion

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
