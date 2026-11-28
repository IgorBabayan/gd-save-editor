using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;

namespace GD.SaveEditor.Tests.Support;

/// <summary>
/// Builds the values of a Grim Dawn file (<c>player.gdc</c>, <c>transfer.gst</c>, <c>reagents.gst</c>) one by one and
/// encrypts them with <see cref="GdcWriter.EncodeSequence"/>, so the readers can be tested on small files made for the test.
/// </summary>
internal sealed class TokenBuilder
{
    /// <summary>Any seed works; a fixed one keeps the tests deterministic.</summary>
    public static readonly byte[] Seed = [0x12, 0x34, 0x56, 0x78];

    private readonly List<GdcToken> _tokens = [];
    private readonly Stack<int> _blocks = new();

    public IReadOnlyList<GdcToken> Tokens => _tokens;

    public TokenBuilder UInt32(uint value)
    {
        ItemContainerTokens.AddUInt32(_tokens, value);
        return this;
    }

    public TokenBuilder Int32(int value) => UInt32(unchecked((uint)value));

    public TokenBuilder Single(float value) => UInt32(BitConverter.SingleToUInt32Bits(value));

    public TokenBuilder KeyUnchanged(uint value)
    {
        _tokens.Add(new GdcToken(0, GdcTokenKind.UInt32KeyUnchanged, value));
        return this;
    }

    public TokenBuilder Byte(byte value)
    {
        _tokens.Add(new GdcToken(0, GdcTokenKind.Byte, value));
        return this;
    }

    public TokenBuilder String(string value)
    {
        ItemContainerTokens.AddString(_tokens, value);
        return this;
    }

    public TokenBuilder Item(SaveItem item)
    {
        ItemContainerTokens.AddItem(_tokens, item);
        return this;
    }

    /// <summary><c>int32 id</c> and a length (key unchanged) that <see cref="EndBlock"/> fills in.</summary>
    public TokenBuilder BeginBlock(uint id)
    {
        UInt32(id);
        _blocks.Push(_tokens.Count);
        return KeyUnchanged(0);
    }

    /// <summary>Sets the length of the open block and writes its end marker (a zero, key unchanged).</summary>
    public TokenBuilder EndBlock()
    {
        var lengthIndex = _blocks.Pop();
        _tokens[lengthIndex] = _tokens[lengthIndex] with
        {
            Value = ItemContainerTokens.SizeOf(_tokens, lengthIndex + 1, _tokens.Count)
        };
        return KeyUnchanged(0);
    }

    public byte[] Build()
    {
        if (_blocks.Count != 0)
            throw new InvalidOperationException("A block was not ended.");
        return GdcWriter.EncodeSequence(Seed, _tokens);
    }

    /// <summary>An item with only a base record, the way the game stores a plain item.</summary>
    public static SaveItem PlainItem(string baseRecord, uint seed = 42, uint stackCount = 1) =>
        new(baseRecord, "", "", "", "", seed, "", "", 0, "", 0, 0, 0, stackCount);
}
