using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;
using GD.SaveEditor.Tests.Support;

namespace GD.SaveEditor.Tests.Saves;

public sealed class ModelTests
{
    private const string Dynamite = "records/items/questitems/quest_dynamite.dbr";

    [Fact]
    public void SaveItem_CreateHasNothingAddedAndAtLeastOneInTheStack()
    {
        var item = SaveItem.Create(Dynamite, seed: 5, quantity: 0);

        Assert.False(item.IsEmpty);
        Assert.Equal(1u, item.Quantity);
        Assert.Empty(item.ExtraFields);
        Assert.Equal("", item.PrefixRecord);
    }

    [Fact]
    public void SaveItem_QuantityLivesInStackCountWithoutExtraFields()
    {
        var item = SaveItem.Create(Dynamite, 1).WithQuantity(352);

        Assert.Equal(352u, item.StackCount);
        Assert.Equal(352u, item.Quantity);
    }

    [Fact]
    public void SaveItem_QuantityMovesIntoTheSecondExtraField()
    {
        var item = SaveItem.Create(Dynamite, 1, quantity: 20).WithExtraFieldCount(4);

        Assert.Equal([0u, 20u, 0u, 0u], item.ExtraFields);
        Assert.Equal(0u, item.StackCount);
        Assert.Equal(20u, item.Quantity);
        Assert.Equal(30u, item.WithQuantity(30).ExtraFields[1]);
    }

    [Fact]
    public void SaveItem_QuantityMovesBackWhenTheExtraFieldsGo()
    {
        var item = SaveItem.Create(Dynamite, 1, quantity: 20).WithExtraFieldCount(4).WithExtraFieldCount(0);

        Assert.Empty(item.ExtraFields);
        Assert.Equal(20u, item.StackCount);
    }

    [Fact]
    public void SaveItem_SameExtraFieldCountReturnsTheSameItem()
    {
        var item = SaveItem.Create(Dynamite, 1).WithExtraFieldCount(4);

        Assert.Same(item, item.WithExtraFieldCount(4));
    }

    [Fact]
    public void SaveItem_EmptyBaseRecordIsAnEmptySlot()
    {
        Assert.True(TokenBuilder.PlainItem("").IsEmpty);
    }

    [Theory]
    [InlineData("Valid Name", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(" Padded", false)]
    [InlineData("ThisNameIsWayTooLongForTheGameField", false)]
    public void CharacterLimits_ValidateName(string name, bool valid)
    {
        Assert.Equal(valid, CharacterLimits.ValidateName(name) is null);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789ABCDEF", true)]
    [InlineData("0123456789abcdef", false)]
    [InlineData("0123456789abcdef0123456789abcdeg", false)]
    [InlineData(null, false)]
    public void Waypoints_ValidUid(string? uid, bool valid)
    {
        Assert.Equal(valid, CharacterWaypoints.IsValidUid(uid));
    }

    [Fact]
    public void CharacterChanges_IsEmptyOnlyWithoutChanges()
    {
        Assert.True(new CharacterChanges().IsEmpty);
        Assert.True(new CharacterChanges(Equipment: [], Factions: [], Profile: new ProfileChanges()).IsEmpty);
        Assert.False(new CharacterChanges(Money: 0).IsEmpty);
        Assert.False(new CharacterChanges(Bags: []).IsEmpty);
        Assert.False(new CharacterChanges(Profile: new ProfileChanges(IsMale: true)).IsEmpty);
    }

    [Fact]
    public void ProfileChanges_TellWhichBlocksChange()
    {
        Assert.True(new ProfileChanges(CrucibleTributes: 1).ChangesInfo);
        Assert.False(new ProfileChanges(CrucibleTributes: 1).ChangesBio);
        Assert.True(new ProfileChanges(Spirit: 10).ChangesBio);
    }

    [Fact]
    public void PlacedItem_CreateIsNew()
    {
        var placed = PlacedItem.Create(SaveItem.Create(Dynamite, 1), 2, 3);

        Assert.True(placed.IsNew);
        Assert.False(new PlacedItem(0, 0, 2, 3).IsNew);
    }

    [Theory]
    [InlineData(5u, false, 5)]
    [InlineData(0x40400000u, true, 3)]   // 3.0f
    [InlineData(0x40600000u, true, 4)]   // 3.5f rounds to even
    public void ItemContainerTokens_DecodeCell(uint value, bool floatCells, int expected)
    {
        Assert.Equal(expected, ItemContainerTokens.DecodeCell(value, floatCells));
    }

    [Fact]
    public void ItemContainerTokens_StringIsALengthAndItsBytes()
    {
        var tokens = new List<GdcToken>();
        ItemContainerTokens.AddString(tokens, "ab");
        ItemContainerTokens.AddString(tokens, null);

        Assert.Equal([2u, (uint)'a', (uint)'b', 0u], tokens.Select(t => t.Value));
        Assert.Equal(4u + 1 + 1 + 4, ItemContainerTokens.SizeOf(tokens, 0, tokens.Count));
    }

    [Fact]
    public void ItemContainerTokens_VerifyFindsADifference()
    {
        GdcToken[] intended = [new(0, GdcTokenKind.UInt32, 1)];

        ItemContainerTokens.Verify(intended, [new(4, GdcTokenKind.UInt32, 1)], "file");
        Assert.Throws<InvalidDataException>(() => ItemContainerTokens.Verify(intended, [new(4, GdcTokenKind.UInt32, 2)], "file"));
        Assert.Throws<InvalidDataException>(() => ItemContainerTokens.Verify(intended, [], "file"));
    }
}
