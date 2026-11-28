using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;
using GD.SaveEditor.Tests.Support;

namespace GD.SaveEditor.Tests.Saves;

public sealed class CharacterSaveEditorTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly CharacterSaveParser _parser = new(new StubGameTexts());
    private readonly CharacterSaveEditor _editor = new();

    public void Dispose() => _folder.Dispose();

    private CharacterSave Load(string fixture) =>
        _parser.Parse(_folder.CreateFile(Path.Combine("main", Path.GetFileName(Path.GetDirectoryName(fixture)!), "player.gdc"),
            Fixtures.Read(fixture)));

    [Fact]
    public void Apply_WithoutChangesReturnsTheFileAsItIs()
    {
        var character = Load(Fixtures.MitraSuric);

        Assert.Same(character.RawData, _editor.Apply(character, new CharacterChanges()));
    }

    [Theory]
    [MemberData(nameof(Fixtures.Characters), MemberType = typeof(Fixtures))]
    public void Apply_ChangesTheIronBits(string fixture)
    {
        var character = Load(fixture);

        var data = _editor.Apply(character, new CharacterChanges(Money: 123_456));

        Assert.Equal(123_456u, CharacterFile.Read(data).Money);
    }

    [Fact]
    public void Apply_ChangesTheNameAndKeepsTheRest()
    {
        var character = Load(Fixtures.MitraSuric);
        var before = CharacterFile.Read(character.RawData);

        var data = _editor.Apply(character, new CharacterChanges(Profile: new ProfileChanges(Name: "A much longer test name")));

        var after = CharacterFile.Read(data);
        Assert.Equal("A much longer test name", after.Name);
        Assert.Equal(before.Level, after.Level);
        Assert.Equal(before.Money, after.Money);
        Assert.Equal(before.Inventory!.Bags.Select(b => b.Items.Count), after.Inventory!.Bags.Select(b => b.Items.Count));
    }

    [Fact]
    public void Apply_ChangesUnspentPoints()
    {
        var character = Load(Fixtures.DarthRevan);

        var data = _editor.Apply(character, new CharacterChanges(Profile: new ProfileChanges(AttributePoints: 7, SkillPoints: 9)));

        var stats = CharacterFile.Read(data).Stats!;
        Assert.Equal(7, stats.AttributePoints);
        Assert.Equal(9, stats.SkillPoints);
    }

    [Fact]
    public void Apply_RejectsAnInvalidName()
    {
        var character = Load(Fixtures.MitraSuric);

        Assert.Throws<InvalidDataException>(() =>
            _editor.Apply(character, new CharacterChanges(Profile: new ProfileChanges(Name: " padded "))));
    }

    [Fact]
    public void Apply_KeepsEveryBagItemWhenTheBagsAreRebuiltInPlace()
    {
        var character = Load(Fixtures.MitraSuric);
        var bags = character.Inventory!.Bags
            .Select((bag, b) => (IReadOnlyList<PlacedItem>)bag.Items.Select((item, i) => new PlacedItem(b, i, item.X, item.Y)).ToList())
            .ToList();

        var data = _editor.Apply(character, new CharacterChanges(Bags: bags));

        Assert.Equal(character.RawData, data);
    }

    [Fact]
    public void Apply_AddsANewItemToTheFirstBag()
    {
        var character = Load(Fixtures.MitraSuric);
        var bags = character.Inventory!.Bags
            .Select((bag, b) => bag.Items.Select((item, i) => new PlacedItem(b, i, item.X, item.Y)).ToList())
            .ToList();
        var ring = SaveItem.Create("records/items/gearaccessories/rings/a01_ring01.dbr", seed: 99);
        bags[0].Add(PlacedItem.Create(ring, 0, 0));

        var data = _editor.Apply(character, new CharacterChanges(Bags: bags.Select(b => (IReadOnlyList<PlacedItem>)b).ToList()));

        var firstBag = CharacterFile.Read(data).Inventory!.Bags[0];
        Assert.Equal(character.Inventory.Bags[0].Items.Count + 1, firstBag.Items.Count);
        Assert.Contains(firstBag.Items, i => i.Item.BaseRecord == ring.BaseRecord && i.Item.Seed == 99);
    }

    private sealed class StubGameTexts : Services.GameData.Database.IGameTextProvider
    {
        public string? GetText(string? tag) => tag is null ? null : $"[{tag}]";

        public void ClearCache()
        {
        }
    }
}
