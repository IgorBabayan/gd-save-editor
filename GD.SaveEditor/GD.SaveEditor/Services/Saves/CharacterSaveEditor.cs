using System.IO;
using GD.SaveEditor.Services.Saves.Models;
using static GD.SaveEditor.Services.Saves.ItemContainerTokens;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Writes changes into the bytes of a character file. Every value of a <c>player.gdc</c> moves the encryption key on,
/// so one changed value changes the encrypted bytes of everything after it: the file is read value by value
/// (<see cref="CharacterFile"/>), the values are changed (the iron bits; the faction reputations; the sex, unlocked difficulty, Crucible tributes,
/// unspent points and attributes) or rebuilt (the name; the bag blocks, when items were moved, added, changed or removed;
/// the equipment slots, when items were equipped, changed or taken off; the personal stash tabs, when their items were
/// moved, added, changed or removed; the waypoint lists, when waypoints were unlocked or locked) and the whole file is encrypted again (<see cref="GdcWriter"/>). The result is read back before it is returned: it
/// must hold exactly the values that were meant to be written.
/// </summary>
public sealed class CharacterSaveEditor : ICharacterSaveEditor
{
    public byte[] Apply(CharacterSave character, CharacterChanges changes)
    {
        if (changes.IsEmpty)
            return character.RawData;

        var original = CharacterFile.Read(character.RawData, recordTokens: true);
        if (original.RewriteError is { } rewriteError)
            throw new InvalidDataException($"{character.CharacterName} cannot be changed by this editor: {rewriteError}");

        GdcWriter.EnsureCoversFile(character.RawData, original.Tokens);
        var tokens = original.Tokens.ToList();

        if (changes.Money is { } money)
            SetValue(tokens, original.MoneyOffset, money, "The iron bits");

        // Every faction entry has the same size: the values are changed in place, like the iron bits.
        if (changes.Factions is { Count: > 0 } factions)
            SetFactions(tokens, original, factions);

        // Values of the header, the character info and the bio block keep their size: only the name (below) changes the
        // number of values, and it is replaced last.
        if (changes.Profile is { IsEmpty: false } profile)
            SetProfileValues(tokens, original, profile);

        // The waypoint block comes after the inventory: rebuilt before the bags and the equipment, it does not move them.
        if (changes.Waypoints is { } waypoints)
        {
            if (original.WaypointLayout is not { } waypointLayout)
                throw new InvalidDataException("The waypoints could not be found in the save file.");
            ReplaceWaypoints(tokens, waypointLayout, waypoints);
        }

        // The stash block (4) is between the inventory and the waypoints: rebuilt after the waypoints and before the
        // inventory, the indices of both stay valid.
        if (changes.Stash is { } stash)
        {
            if (original.StashLayout is not { } stashLayout)
                throw new InvalidDataException("The personal stash could not be found in the save file.");
            var delta = Rebuild(tokens, stashLayout.Tabs.Select(ContainerTokenLayout.From).ToList(), stash,
                original.ItemExtraFields, floatCells: true, "stash tabs");
            tokens[stashLayout.LengthToken] = tokens[stashLayout.LengthToken] with
            {
                Value = SizeOf(tokens, stashLayout.LengthToken + 1, stashLayout.EndToken + delta)
            };
        }

        // The equipment is stored after the bags, so it is replaced first: the bags' indices stay valid. The inventory
        // block that holds both grows or shrinks by what they changed.
        var inventoryDelta = 0;
        int? inventoryLengthToken = null, inventoryEndToken = null;
        if (changes.Equipment is { Count: > 0 } equipment)
        {
            if (original.EquipmentLayout is not { } equipmentLayout)
                throw new InvalidDataException("The equipment could not be found in the save file.");
            inventoryDelta += ReplaceEquipment(tokens, original, equipmentLayout, equipment);
            (inventoryLengthToken, inventoryEndToken) = (equipmentLayout.LengthToken, equipmentLayout.EndToken);
        }

        if (changes.Bags is { } bags)
        {
            if (original.InventoryLayout is not { } layout)
                throw new InvalidDataException("The bags could not be found in the save file.");
            inventoryDelta += RebuildBags(tokens, original, layout, bags);
            (inventoryLengthToken, inventoryEndToken) = (layout.LengthToken, layout.EndToken);
        }

        if (inventoryLengthToken is { } lengthToken && inventoryEndToken is { } endToken)
            tokens[lengthToken] = tokens[lengthToken] with { Value = SizeOf(tokens, lengthToken + 1, endToken + inventoryDelta) };

        // The name is in the header, before every block: a longer or shorter name moves all the values after it, but no
        // block length covers it. Done last, since it moves the indices used above.
        if (changes.Profile?.Name is { } name && name != original.Name)
            ReplaceName(tokens, original, name);

        var data = GdcWriter.EncodeSequence(character.RawData, tokens);
        Verify(tokens, CharacterFile.Read(data, recordTokens: true));
        return data;
    }

    /// <summary>
    /// Sets the values of <paramref name="profile"/> that keep their size: the "male" flag of the header, the unlocked
    /// difficulty and the Crucible tributes of the character info, the unspent points and the attributes of the bio
    /// block (see <see cref="CharacterFile"/>).
    /// </summary>
    private static void SetProfileValues(List<GdcToken> tokens, CharacterFile original, ProfileChanges profile)
    {
        if (profile.Name is { } name && CharacterLimits.ValidateName(name) is { } nameError)
            throw new InvalidDataException(nameError);

        if (profile.IsMale is { } isMale)
            SetValue(tokens, original.IsMaleOffset, isMale ? 1u : 0u, "The sex of the character");

        if (profile.UnlockedDifficulty is { } unlocked)
        {
            if (!Enum.IsDefined(unlocked))
                throw new InvalidDataException($"Unknown difficulty {(int)unlocked}.");
            SetValue(tokens, original.GreatestDifficultyOffset, (uint)unlocked, "The unlocked difficulty");

            // The difficulty the character plays (low bits; the game keeps other flags above them) cannot be above the
            // unlocked one: it is lowered with it, so the game does not load the character into a locked difficulty.
            var index = Find(tokens, original.DifficultyOffset, "The current difficulty");
            var current = tokens[index].Value;
            if ((current & 0x0F) > (uint)unlocked)
                tokens[index] = tokens[index] with { Value = (current & 0xF0) | (uint)unlocked };
        }

        if (profile.CrucibleTributes is { } tributes)
        {
            if (tributes > CharacterLimits.MaxTributes)
                throw new InvalidDataException($"The Crucible tributes cannot be more than {CharacterLimits.MaxTributes:N0}.");
            SetValue(tokens, original.TributesOffset, tributes, "The Crucible tributes");
        }

        if (!profile.ChangesBio)
            return;

        // Bio block: level, experience, attribute points, skill points, devotion points, total devotion, physique,
        // cunning, spirit, health, energy, one value each.
        var bio = Find(tokens, original.StatsOffset, "The attributes");
        if (bio + 10 >= tokens.Count)
            throw new InvalidDataException("The attributes could not be found in the save file.");

        SetPoints(tokens, bio + 2, profile.AttributePoints, CharacterLimits.MaxPoints, "Unspent attribute points");
        SetPoints(tokens, bio + 3, profile.SkillPoints, CharacterLimits.MaxPoints, "Unspent skill points");
        SetPoints(tokens, bio + 4, profile.DevotionPoints, CharacterLimits.MaxDevotionPoints, "Unspent devotion points");

        // Base health and energy are stored with the attributes: they move by what the game gives per point.
        SetAttribute(tokens, bio + 6, profile.Physique, "Physique", bio + 9, CharacterLimits.HealthPerPhysique);
        SetAttribute(tokens, bio + 7, profile.Cunning, "Cunning");
        SetAttribute(tokens, bio + 8, profile.Spirit, "Spirit", bio + 10, CharacterLimits.EnergyPerSpirit);
    }

    private static void SetPoints(List<GdcToken> tokens, int index, int? value, int max, string what)
    {
        if (value is not { } points)
            return;
        if (points < 0 || points > max)
            throw new InvalidDataException($"{what} must be from 0 to {max:N0}.");
        tokens[index] = tokens[index] with { Value = (uint)points };
    }

    /// <summary>Sets an attribute (a float); the value at <paramref name="derivedIndex"/> moves by <paramref name="perPoint"/> per point.</summary>
    private static void SetAttribute(
        List<GdcToken> tokens, int index, int? value, string what, int derivedIndex = -1, float perPoint = 0)
    {
        if (value is not { } attribute)
            return;
        if (attribute < 0 || attribute > CharacterLimits.MaxAttribute)
            throw new InvalidDataException($"{what} must be from 0 to {CharacterLimits.MaxAttribute:N0}.");

        var previous = BitConverter.UInt32BitsToSingle(tokens[index].Value);
        tokens[index] = tokens[index] with { Value = BitConverter.SingleToUInt32Bits(attribute) };

        if (derivedIndex < 0)
            return;
        var derived = BitConverter.UInt32BitsToSingle(tokens[derivedIndex].Value) + (attribute - previous) * perPoint;
        tokens[derivedIndex] = tokens[derivedIndex] with { Value = BitConverter.SingleToUInt32Bits(Math.Max(1f, derived)) };
    }

    /// <summary>
    /// Writes the reputation of <paramref name="changes"/> into the faction list (block 13) and marks every changed faction
    /// as modified and unlocked, as the game does once a faction's reputation moved.
    /// </summary>
    private static void SetFactions(List<GdcToken> tokens, CharacterFile original, IReadOnlyList<FactionChange> changes)
    {
        if (original.FactionOffsets is not { } offsets)
            throw new InvalidDataException("The factions could not be found in the save file.");
        if (changes.Select(c => c.Index).Distinct().Count() != changes.Count)
            throw new InvalidDataException("A faction was changed twice.");

        foreach (var change in changes)
        {
            if (change.Index < 0 || change.Index >= offsets.Count)
                throw new InvalidDataException($"The save file has no faction {change.Index}.");
            if (!float.IsFinite(change.Value) || change.Value < CharacterFactions.MinValue || change.Value > CharacterFactions.MaxValue)
                throw new InvalidDataException(
                    $"A faction's reputation must be from {CharacterFactions.MinValue:N0} to {CharacterFactions.MaxValue:N0}.");

            var faction = offsets[change.Index];
            SetValue(tokens, faction.Value, BitConverter.SingleToUInt32Bits(change.Value), "The faction reputation");
            SetValue(tokens, faction.Modified, 1u, "The faction reputation");
            SetValue(tokens, faction.Unlocked, 1u, "The faction reputation");
        }
    }

    /// <summary>
    /// Replaces the name of the header: <c>int32 length, length x (2 bytes)</c> (UTF-16, like
    /// <see cref="GdcReader.ReadWideString"/> reads it).
    /// </summary>
    private static void ReplaceName(List<GdcToken> tokens, CharacterFile original, string name)
    {
        if (CharacterLimits.ValidateName(name) is { } error)
            throw new InvalidDataException(error);

        var index = Find(tokens, original.NameOffset, "The name");
        var length = (int)tokens[index].Value;
        if (length != original.Name.Length || index + 1 + length * 2 > tokens.Count)
            throw new InvalidDataException("The name could not be found in the save file.");

        var values = new List<GdcToken>(1 + name.Length * 2);
        AddUInt32(values, (uint)name.Length);
        foreach (var c in name)
        {
            values.Add(new GdcToken(0, GdcTokenKind.Byte, (uint)(c & 0xFF)));
            values.Add(new GdcToken(0, GdcTokenKind.Byte, (uint)(c >> 8)));
        }

        tokens.RemoveRange(index, 1 + length * 2);
        tokens.InsertRange(index, values);
    }

    /// <summary>Sets the value read at <paramref name="offset"/> (the tokens before any value was added or removed).</summary>
    private static void SetValue(List<GdcToken> tokens, int? offset, uint value, string what)
    {
        var index = Find(tokens, offset, what);
        tokens[index] = tokens[index] with { Value = value };
    }

    /// <summary>Index of the value read at <paramref name="offset"/>.</summary>
    /// <exception cref="InvalidDataException">That part of the file was not read.</exception>
    private static int Find(List<GdcToken> tokens, int? offset, string what)
    {
        var index = offset is { } value ? tokens.FindIndex(t => t.Offset == value) : -1;
        return index >= 0 ? index : throw new InvalidDataException($"{what} could not be found in the save file.");
    }

    /// <summary>
    /// Replaces the three waypoint lists of block 6 (<c>int32 count, count x 16-byte id</c> each) with
    /// <paramref name="waypoints"/> and computes the block's length again.
    /// </summary>
    private static void ReplaceWaypoints(List<GdcToken> tokens, WaypointTokenLayout layout, CharacterWaypoints waypoints)
    {
        if (waypoints.ByDifficulty.Count != CharacterWaypoints.DifficultyCount || layout.Lists.Count != CharacterWaypoints.DifficultyCount)
            throw new InvalidDataException($"The waypoints must have {CharacterWaypoints.DifficultyCount} lists (Normal, Elite, Ultimate).");

        var segment = new List<GdcToken>();
        for (var list = 0; list < CharacterWaypoints.DifficultyCount; list++)
        {
            var uids = waypoints.ByDifficulty[list];
            if (uids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != uids.Count)
                throw new InvalidDataException("A waypoint is listed twice.");

            segment.Add(tokens[layout.Lists[list].CountToken] with { Value = (uint)uids.Count });
            foreach (var uid in uids)
            {
                if (!CharacterWaypoints.IsValidUid(uid))
                    throw new InvalidDataException($"\"{uid}\" is not a waypoint id.");
                foreach (var b in Convert.FromHexString(uid))
                    segment.Add(new GdcToken(0, GdcTokenKind.Byte, b));
            }
        }

        // The lists are stored one after another, from the first count to the end of the last list.
        var first = layout.Lists[0].CountToken;
        var count = layout.Lists[^1].End - first;
        tokens.RemoveRange(first, count);
        tokens.InsertRange(first, segment);

        var endToken = layout.EndToken + segment.Count - count;
        tokens[layout.LengthToken] = tokens[layout.LengthToken] with { Value = SizeOf(tokens, layout.LengthToken + 1, endToken) };
    }

    /// <summary>
    /// Writes the items of <paramref name="changes"/> into their equipment slots: the slot's item values are replaced
    /// (<see cref="AddItem"/>; an empty item for a slot that is emptied) and its "attached" flag is set as the game sets
    /// it (1 with an item, 0 without). Returns how many values the inventory block gained (negative: lost).
    /// </summary>
    private static int ReplaceEquipment(
        List<GdcToken> tokens, CharacterFile original, EquipmentTokenLayout layout, IReadOnlyList<EquipmentChange> changes)
    {
        var slots = new List<(SlotTokenLayout Slot, SaveItem? Item)>(changes.Count);
        foreach (var change in changes)
        {
            var slot = layout.Slots.FirstOrDefault(s => s.Slot == change.Slot && s.WeaponSet == change.WeaponSet)
                       ?? throw new InvalidDataException($"The save file has no {change.Slot} slot for weapon set {change.WeaponSet}.");
            if (slots.Any(s => ReferenceEquals(s.Slot, slot)))
                throw new InvalidDataException($"The {change.Slot} slot was changed twice.");
            if (change.Item is { IsEmpty: true })
                throw new InvalidDataException("An equipped item has no record.");
            slots.Add((slot, change.Item));
        }

        // From the last slot to the first, so the indices of the slots not replaced yet stay valid.
        var delta = 0;
        foreach (var (slot, item) in slots.OrderByDescending(s => s.Slot.Start))
        {
            var values = new List<GdcToken>();
            // An empty slot holds an empty item: no records, zeros, a stack of one (as the game writes it).
            AddItem(values, (item ?? SaveItem.Create(string.Empty, 0)).WithExtraFieldCount(original.ItemExtraFields));

            tokens[slot.AttachedToken] = tokens[slot.AttachedToken] with { Value = item is null ? 0u : 1u };
            tokens.RemoveRange(slot.Start, slot.End - slot.Start);
            tokens.InsertRange(slot.Start, values);
            delta += values.Count - (slot.End - slot.Start);
        }

        return delta;
    }

    /// <summary>
    /// Replaces the bag blocks in <paramref name="tokens"/> with blocks holding the items as placed in
    /// <paramref name="bags"/> (see <see cref="ItemContainerTokens.Rebuild"/>). Returns how many values the inventory
    /// block gained (negative: lost), for its length.
    /// </summary>
    private static int RebuildBags(
        List<GdcToken> tokens, CharacterFile original, InventoryTokenLayout layout, IReadOnlyList<IReadOnlyList<PlacedItem>> bags) =>
        Rebuild(tokens, layout.Bags.Select(ContainerTokenLayout.From).ToList(), bags, original.ItemExtraFields,
            floatCells: false, "bags");

    /// <summary>The written file must read back with exactly the values that were meant to be written.</summary>
    private static void Verify(IReadOnlyList<GdcToken> intended, CharacterFile written)
    {
        if (written.RewriteError is not null || written.DetailsError is not null || written.Tokens.Count != intended.Count)
            throw new InvalidDataException("The changed save file did not read back correctly; nothing was written.");

        for (var i = 0; i < intended.Count; i++)
        {
            if (written.Tokens[i].Kind != intended[i].Kind || written.Tokens[i].Value != intended[i].Value)
                throw new InvalidDataException($"The changed save file differs at offset {written.Tokens[i].Offset}; nothing was written.");
        }
    }
}
