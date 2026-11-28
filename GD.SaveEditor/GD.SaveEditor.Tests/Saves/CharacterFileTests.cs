using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Tests.Support;

namespace GD.SaveEditor.Tests.Saves;

public sealed class CharacterFileTests
{
    [Theory]
    [MemberData(nameof(Fixtures.Characters), MemberType = typeof(Fixtures))]
    public void Read_ReadsTheHeaderAndTheDetails(string path)
    {
        var file = CharacterFile.Read(Fixtures.Read(path));

        Assert.False(string.IsNullOrWhiteSpace(file.Name));
        Assert.InRange(file.Level, 1, 100);
        Assert.Null(file.DetailsError);
        Assert.NotNull(file.Stats);
        Assert.NotNull(file.Inventory);
        Assert.NotNull(file.Equipment);
        Assert.NotNull(file.Money);
        Assert.NotNull(file.Waypoints);
    }

    [Theory]
    [MemberData(nameof(Fixtures.Characters), MemberType = typeof(Fixtures))]
    public void Read_RecordsEveryValueSoTheFileCanBeWrittenAgain(string path)
    {
        var data = Fixtures.Read(path);
        var file = CharacterFile.Read(data, recordTokens: true);

        Assert.Null(file.RewriteError);
        GdcWriter.EnsureCoversFile(data, file.Tokens);
        Assert.Equal(data, GdcWriter.Encode(data, file.Tokens, new Dictionary<int, uint>()));
    }

    [Theory]
    [MemberData(nameof(Fixtures.Characters), MemberType = typeof(Fixtures))]
    public void Read_FindsTheLayoutsOfTheEditableParts(string path)
    {
        var file = CharacterFile.Read(Fixtures.Read(path), recordTokens: true);

        Assert.NotNull(file.InventoryLayout);
        Assert.Equal(file.Inventory!.Bags.Count, file.InventoryLayout!.Bags.Count);
        Assert.NotNull(file.EquipmentLayout);
        Assert.NotNull(file.WaypointLayout);
        Assert.Equal(3, file.Waypoints!.ByDifficulty.Count);
    }

    [Fact]
    public void Read_RejectsAFileThatIsNotACharacter()
    {
        Assert.ThrowsAny<InvalidDataException>(() => CharacterFile.Read(Fixtures.Read(Fixtures.MaterialStorage)));
    }

    [Fact]
    public void Read_RejectsATruncatedCharacter()
    {
        var data = Fixtures.Read(Fixtures.MitraSuric);

        Assert.ThrowsAny<Exception>(() =>
        {
            var file = CharacterFile.Read(data[..64]);
            // A file cut inside the header cannot be listed; one cut later must at least report the problem.
            if (file.DetailsError is null)
                throw new InvalidDataException("not detected");
        });
    }
}
