using System.IO;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Services.Saves;

/// <summary>
/// Writes changes into the bytes of a character file. Every value of a <c>player.gdc</c> moves the encryption key on,
/// so one changed value changes the encrypted bytes of everything after it: the file is read value by value
/// (<see cref="CharacterFile"/>), the changed values are put in and the whole file is encrypted again
/// (<see cref="GdcWriter"/>). The result is read back before it is returned: it must hold exactly the values of the
/// original file apart from the changed ones.
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

        var values = new Dictionary<int, uint>();
        if (changes.Money is { } money)
        {
            if (original.MoneyOffset is not { } offset)
                throw new InvalidDataException($"The iron bits of {character.CharacterName} could not be found in the save file.");
            values[offset] = money;
        }

        var data = GdcWriter.Encode(character.RawData, original.Tokens, values);

        var written = CharacterFile.Read(data, recordTokens: true);
        Verify(original, written, values);
        if (changes.Money is { } expected && written.Money != expected)
            throw new InvalidDataException("The changed save file did not read back with the new iron bits; nothing was written.");

        return data;
    }

    /// <summary>The written file must have the same values at the same places as the original, except the changed ones.</summary>
    private static void Verify(CharacterFile original, CharacterFile written, IReadOnlyDictionary<int, uint> changes)
    {
        if (written.RewriteError is not null || written.DetailsError is not null || written.Tokens.Count != original.Tokens.Count)
            throw new InvalidDataException("The changed save file did not read back correctly; nothing was written.");

        for (var i = 0; i < original.Tokens.Count; i++)
        {
            var before = original.Tokens[i];
            var after = written.Tokens[i];
            var expected = changes.TryGetValue(before.Offset, out var changed) ? before with { Value = changed } : before;
            if (after != expected)
                throw new InvalidDataException($"The changed save file differs at offset {before.Offset}; nothing was written.");
        }
    }
}
