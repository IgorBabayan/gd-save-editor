using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Tests.Support;

namespace GD.SaveEditor.Tests.Saves;

public sealed class GdcCodecTests
{
    private static byte[] SampleFile() => new TokenBuilder()
        .UInt32(0xDEADBEEF)
        .KeyUnchanged(7)
        .Byte(0xAB)
        .String("records/items/test.dbr")
        .Single(2.5f)
        .Int32(-3)
        .Build();

    [Fact]
    public void Reader_DecryptsEveryKindOfValue()
    {
        var reader = new GdcReader(SampleFile());

        Assert.Equal(0xDEADBEEFu, reader.ReadUInt32());
        Assert.Equal(7u, reader.ReadUInt32KeyUnchanged());
        Assert.Equal(0xAB, reader.ReadByte());
        Assert.Equal("records/items/test.dbr", reader.ReadString());
        Assert.Equal(2.5f, reader.ReadSingle());
        Assert.Equal(-3, reader.ReadInt32());
        Assert.Equal(reader.Length, reader.Position);
    }

    [Fact]
    public void Reader_KeepsTheSeedUnencrypted()
    {
        var data = SampleFile();

        Assert.Equal(TokenBuilder.Seed, data[..4]);
    }

    [Fact]
    public void Reader_RecordsTokensOnlyWhenAsked()
    {
        var data = SampleFile();

        var plain = new GdcReader(data);
        plain.ReadUInt32();
        Assert.Empty(plain.Tokens);
        Assert.Equal(0, plain.TokenCount);

        var recording = new GdcReader(data, recordTokens: true);
        recording.ReadUInt32();
        recording.ReadUInt32KeyUnchanged();
        Assert.Equal(
            [new GdcToken(4, GdcTokenKind.UInt32, 0xDEADBEEF), new GdcToken(8, GdcTokenKind.UInt32KeyUnchanged, 7)],
            recording.Tokens);
    }

    [Fact]
    public void Reader_RestoreGoesBackAndForgetsTokens()
    {
        var reader = new GdcReader(SampleFile(), recordTokens: true);
        reader.ReadUInt32();
        var state = reader.Snapshot();

        reader.ReadUInt32KeyUnchanged();
        reader.ReadByte();
        reader.Restore(state);

        Assert.Equal(1, reader.TokenCount);
        Assert.Equal(7u, reader.ReadUInt32KeyUnchanged());
        Assert.Equal(0xAB, reader.ReadByte());
    }

    [Fact]
    public void Reader_ThrowsAtTheEndOfTheFile()
    {
        var reader = new GdcReader(new TokenBuilder().Byte(1).Build());
        reader.ReadByte();

        Assert.Throws<InvalidDataException>(() => reader.ReadUInt32());
    }

    [Fact]
    public void Reader_RejectsAStringLongerThanTheFile()
    {
        var reader = new GdcReader(new TokenBuilder().UInt32(1000).Byte(65).Build());

        Assert.Throws<InvalidDataException>(() => reader.ReadString());
    }

    [Fact]
    public void Reader_ReadsWideStrings()
    {
        var data = new TokenBuilder().UInt32(2).Byte(0x41).Byte(0x00).Byte(0x16).Byte(0x04).Build();

        Assert.Equal("AЖ", new GdcReader(data).ReadWideString());
    }

    [Fact]
    public void Reader_ReadsBlocksAndChecksTheirEnd()
    {
        var data = new TokenBuilder().BeginBlock(9).UInt32(1).UInt32(2).EndBlock().Build();
        var reader = new GdcReader(data);

        var (id, end) = reader.ReadBlockStart();
        Assert.Equal(9u, id);
        reader.ReadUInt32();
        Assert.Throws<InvalidDataException>(() => reader.ReadBlockEnd(end));

        reader.ReadUInt32();
        reader.ReadBlockEnd(end);
        Assert.Equal(data.Length, reader.Position);
    }

    [Fact]
    public void Reader_RejectsABlockLongerThanTheFile()
    {
        var data = new TokenBuilder().UInt32(1).KeyUnchanged(10_000).Build();

        Assert.Throws<InvalidDataException>(() => new GdcReader(data).ReadBlockStart());
    }

    [Fact]
    public void Writer_EncodesAnUnchangedFileByteForByte()
    {
        var data = SampleFile();
        var tokens = ReadAll(data);

        Assert.Equal(data, GdcWriter.Encode(data, tokens, new Dictionary<int, uint>()));
    }

    [Fact]
    public void Writer_ChangesOneValueAndReEncryptsEverythingAfterIt()
    {
        var data = SampleFile();
        var tokens = ReadAll(data);

        var changed = GdcWriter.Encode(data, tokens, new Dictionary<int, uint> { [4] = 0x01020304 });

        var reader = new GdcReader(changed);
        Assert.Equal(0x01020304u, reader.ReadUInt32());
        Assert.Equal(7u, reader.ReadUInt32KeyUnchanged());
        Assert.Equal(0xAB, reader.ReadByte());
        Assert.Equal("records/items/test.dbr", reader.ReadString());
        // The key depends on the raw bytes, so the bytes after the change differ too.
        Assert.NotEqual(data[12..], changed[12..]);
    }

    [Fact]
    public void Writer_RejectsAChangeThatIsNotAtAValue()
    {
        var data = SampleFile();

        Assert.Throws<InvalidDataException>(() =>
            GdcWriter.Encode(data, ReadAll(data), new Dictionary<int, uint> { [5] = 1 }));
    }

    [Fact]
    public void Writer_RejectsTokensThatDoNotCoverTheFile()
    {
        var data = SampleFile();
        var tokens = ReadAll(data).ToList();
        tokens.RemoveAt(tokens.Count - 1);

        Assert.Throws<InvalidDataException>(() => GdcWriter.EnsureCoversFile(data, tokens));
    }

    [Fact]
    public void Writer_RejectsAByteValueThatDoesNotFit()
    {
        Assert.Throws<InvalidDataException>(() =>
            GdcWriter.EncodeSequence(TokenBuilder.Seed, [new GdcToken(0, GdcTokenKind.Byte, 256)]));
    }

    [Fact]
    public void Writer_RejectsAFileWithoutSeed()
    {
        Assert.Throws<InvalidDataException>(() => GdcWriter.EncodeSequence([1, 2], []));
    }

    [Fact]
    public void GdcToken_SizeMatchesItsKind()
    {
        Assert.Equal(1, new GdcToken(0, GdcTokenKind.Byte, 0).Size);
        Assert.Equal(4, new GdcToken(0, GdcTokenKind.UInt32, 0).Size);
        Assert.Equal(4, new GdcToken(0, GdcTokenKind.UInt32KeyUnchanged, 0).Size);
    }

    private static IReadOnlyList<GdcToken> ReadAll(byte[] data)
    {
        var reader = new GdcReader(data, recordTokens: true);
        reader.ReadUInt32();
        reader.ReadUInt32KeyUnchanged();
        reader.ReadByte();
        reader.ReadString();
        reader.ReadSingle();
        reader.ReadInt32();
        return reader.Tokens;
    }
}
