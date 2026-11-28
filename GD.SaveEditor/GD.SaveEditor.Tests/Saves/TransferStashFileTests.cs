using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;
using GD.SaveEditor.Tests.Support;

namespace GD.SaveEditor.Tests.Saves;

public sealed class TransferStashFileTests
{
    private const string Sword = "records/items/gearweapons/swords1h/a01_sword01.dbr";
    private const string Ring = "records/items/gearaccessories/rings/a01_ring01.dbr";

    /// <summary>A version 5 transfer stash (no extra item fields) with the sacks given as (width, height, items).</summary>
    private static byte[] Build(params (int Width, int Height, (string Record, float X, float Y)[] Items)[] sacks)
    {
        var builder = new TokenBuilder().UInt32(2).BeginBlock(18).Int32(5).KeyUnchanged(0).String("").Byte(3).Int32(sacks.Length);
        foreach (var (width, height, items) in sacks)
        {
            builder.BeginBlock(0).Int32(width).Int32(height).Int32(items.Length);
            foreach (var (record, x, y) in items)
                builder.Item(TokenBuilder.PlainItem(record)).Single(x).Single(y);
            builder.EndBlock();
        }

        return builder.EndBlock().Build();
    }

    [Fact]
    public void Read_ReturnsSacksAndItems()
    {
        var file = TransferStashFile.Read(Build((10, 18, [(Sword, 1, 2)]), (8, 16, [])));

        Assert.Equal(5, file.Version);
        Assert.Equal(2, file.Sacks.Count);
        Assert.Equal((10, 18), (file.Sacks[0].Width, file.Sacks[0].Height));
        var item = Assert.Single(file.Sacks[0].Items);
        Assert.Equal(Sword, item.Item.BaseRecord);
        Assert.Equal((1f, 2f), (item.X, item.Y));
        Assert.Empty(file.Sacks[1].Items);
        Assert.Equal(0, file.ItemExtraFields);
        Assert.True(file.IsItemLayoutKnown);
    }

    [Fact]
    public void Read_WithoutItemsTakesTheHint()
    {
        var file = TransferStashFile.Read(Build((10, 18, [])), extraFieldsHint: 4);

        Assert.Equal(4, file.ItemExtraFields);
        Assert.False(file.IsItemLayoutKnown);
    }

    [Fact]
    public void Read_RejectsOtherFiles()
    {
        Assert.Throws<InvalidDataException>(() => TransferStashFile.Read([0, 0, 0]));
        Assert.Throws<InvalidDataException>(() => TransferStashFile.Read(MaterialStorageFileTests.Build()));
    }

    [Fact]
    public void Rebuild_MovesAnItemToAnotherSack()
    {
        var data = Build((10, 18, [(Sword, 1, 2)]), (8, 16, []));
        var file = TransferStashFile.Read(data, recordTokens: true);

        var written = Rebuild(data, file, [[], [new PlacedItem(0, 0, 3, 4)]]);

        var reread = TransferStashFile.Read(written);
        Assert.Empty(reread.Sacks[0].Items);
        var moved = Assert.Single(reread.Sacks[1].Items);
        Assert.Equal(Sword, moved.Item.BaseRecord);
        Assert.Equal((3f, 4f), (moved.X, moved.Y));
    }

    [Fact]
    public void Rebuild_AddsAndRemovesItems()
    {
        var data = Build((10, 18, [(Sword, 1, 2)]));
        var file = TransferStashFile.Read(data, recordTokens: true);

        var written = Rebuild(data, file, [[PlacedItem.Create(SaveItem.Create(Ring, seed: 7), 5, 6)]]);

        var item = Assert.Single(TransferStashFile.Read(written).Sacks[0].Items);
        Assert.Equal(Ring, item.Item.BaseRecord);
        Assert.Equal(7u, item.Item.Seed);
    }

    [Fact]
    public void Rebuild_UnchangedPlacementKeepsTheFileAsItWas()
    {
        var data = Build((10, 18, [(Sword, 1, 2), (Ring, 4, 0)]));
        var file = TransferStashFile.Read(data, recordTokens: true);

        Assert.Equal(data, Rebuild(data, file, [[new PlacedItem(0, 0, 1, 2), new PlacedItem(0, 1, 4, 0)]]));
    }

    [Fact]
    public void Rebuild_RejectsAnItemPlacedTwice()
    {
        var data = Build((10, 18, [(Sword, 1, 2)]));
        var file = TransferStashFile.Read(data, recordTokens: true);

        Assert.Throws<InvalidDataException>(() =>
            Rebuild(data, file, [[new PlacedItem(0, 0, 1, 2), new PlacedItem(0, 0, 5, 5)]]));
    }

    [Fact]
    public void Rebuild_RejectsAWrongNumberOfSacks()
    {
        var data = Build((10, 18, []));
        var file = TransferStashFile.Read(data, recordTokens: true);

        Assert.Throws<InvalidDataException>(() => Rebuild(data, file, [[], []]));
    }

    [Fact]
    public void Rebuild_RejectsANegativeCell()
    {
        var data = Build((10, 18, [(Sword, 1, 2)]));
        var file = TransferStashFile.Read(data, recordTokens: true);

        Assert.Throws<InvalidDataException>(() => Rebuild(data, file, [[new PlacedItem(0, 0, -1, 0)]]));
    }

    [Fact]
    public void Fixture_RoundTripsByteForByte()
    {
        var data = Fixtures.Read(Fixtures.TransferStash);
        var file = TransferStashFile.Read(data, recordTokens: true);

        Assert.NotEmpty(file.Sacks);
        GdcWriter.EnsureCoversFile(data, file.Tokens);
        Assert.Equal(data, GdcWriter.Encode(data, file.Tokens, new Dictionary<int, uint>()));
    }

    [Fact]
    public void Fixture_KeepsEveryItemWhenRebuiltInPlace()
    {
        var data = Fixtures.Read(Fixtures.TransferStash);
        var file = TransferStashFile.Read(data, recordTokens: true);
        var placed = file.Sacks
            .Select((sack, s) => (IReadOnlyList<PlacedItem>)sack.Items
                .Select((item, i) => new PlacedItem(s, i, (int)MathF.Round(item.X), (int)MathF.Round(item.Y)))
                .ToList())
            .ToList();

        var written = Rebuild(data, file, placed);

        // Every item keeps all its values and its cell: the file comes out as it was read.
        Assert.Equal(data, written);
    }

    /// <summary>Rebuilds the sacks like the transfer stash service does and fixes the length of the stash block.</summary>
    private static byte[] Rebuild(byte[] data, TransferStashFile file, IReadOnlyList<IReadOnlyList<PlacedItem>> sacks)
    {
        var layout = file.Layout!;
        var tokens = file.Tokens.ToList();
        var delta = ItemContainerTokens.Rebuild(tokens, layout.Tabs.Select(ContainerTokenLayout.From).ToList(), sacks,
            file.ItemExtraFields, floatCells: true, "sacks");
        tokens[layout.LengthToken] = tokens[layout.LengthToken] with
        {
            Value = ItemContainerTokens.SizeOf(tokens, layout.LengthToken + 1, layout.EndToken + delta)
        };

        var written = GdcWriter.EncodeSequence(data, tokens);
        ItemContainerTokens.Verify(tokens, TransferStashFile.Read(written, recordTokens: true).Tokens, "transfer stash");
        return written;
    }
}
