using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Saves.Models;
using GD.SaveEditor.Tests.Support;

namespace GD.SaveEditor.Tests.Saves;

public sealed class MaterialStorageFileTests
{
    private const string Crystal = "records/items/materia/compa_aethercrystal.dbr";
    private const string Scrap = "records/items/crafting/materials/craft_scrapmetal.dbr";

    internal static byte[] Build(params (string Record, int Amount)[] materials)
    {
        var builder = new TokenBuilder().UInt32(1).BeginBlock(20).Int32(1).KeyUnchanged(0).String("").Int32(materials.Length);
        foreach (var (record, amount) in materials)
            builder.BeginBlock(0).String(record).Int32(amount).EndBlock();
        return builder.EndBlock().Build();
    }

    [Fact]
    public void Read_ReturnsTheMaterialsInFileOrder()
    {
        var file = MaterialStorageFile.Read(Build((Crystal, 12), (Scrap, 0)));

        Assert.Equal(1, file.Version);
        Assert.Equal("", file.Mod);
        Assert.Equal([new StoredMaterial(Crystal, 12), new StoredMaterial(Scrap, 0)], file.Materials);
        Assert.Empty(file.Tokens);
    }

    [Fact]
    public void WithMaterials_WritesAFileThatReadsBack()
    {
        var data = Build((Crystal, 12));
        var file = MaterialStorageFile.Read(data, recordTokens: true);
        StoredMaterial[] changed = [new(Crystal, 999), new(Scrap, 5)];

        var written = GdcWriter.EncodeSequence(data, file.WithMaterials(changed));

        Assert.Equal(changed, MaterialStorageFile.Read(written).Materials);
    }

    [Fact]
    public void WithMaterials_UnchangedListKeepsTheFileAsItWas()
    {
        var data = Build((Crystal, 12), (Scrap, 3));
        var file = MaterialStorageFile.Read(data, recordTokens: true);

        Assert.Equal(data, GdcWriter.EncodeSequence(data, file.WithMaterials(file.Materials)));
    }

    [Fact]
    public void WithMaterials_RejectsNegativeAmounts()
    {
        var file = MaterialStorageFile.Read(Build((Crystal, 1)), recordTokens: true);

        Assert.Throws<InvalidDataException>(() => file.WithMaterials([new StoredMaterial(Crystal, -1)]));
    }

    [Fact]
    public void WithMaterials_NeedsTheRecordedValues()
    {
        var file = MaterialStorageFile.Read(Build((Crystal, 1)));

        Assert.Throws<InvalidOperationException>(() => file.WithMaterials([]));
    }

    [Fact]
    public void Read_RejectsOtherFiles()
    {
        Assert.Throws<InvalidDataException>(() => MaterialStorageFile.Read([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => MaterialStorageFile.Read(new TokenBuilder().UInt32(2).UInt32(0).UInt32(0).Build()));
        Assert.Throws<InvalidDataException>(() =>
            MaterialStorageFile.Read(new TokenBuilder().UInt32(1).BeginBlock(20).Int32(2).KeyUnchanged(0).String("").Int32(0).EndBlock().Build()));
    }

    [Fact]
    public void Read_RejectsAMaterialThatIsNotARecord()
    {
        Assert.Throws<InvalidDataException>(() => MaterialStorageFile.Read(Build(("items/x.dbr", 1))));
    }

    [Fact]
    public void Fixture_RoundTripsByteForByte()
    {
        var data = Fixtures.Read(Fixtures.MaterialStorage);
        var file = MaterialStorageFile.Read(data, recordTokens: true);

        Assert.NotEmpty(file.Materials);
        Assert.All(file.Materials, m => Assert.StartsWith("records/", m.RecordPath, StringComparison.OrdinalIgnoreCase));
        GdcWriter.EnsureCoversFile(data, file.Tokens);
        Assert.Equal(data, GdcWriter.EncodeSequence(data, file.WithMaterials(file.Materials)));
    }

    [Fact]
    public void Fixture_ChangedAmountReadsBack()
    {
        var data = Fixtures.Read(Fixtures.MaterialStorage);
        var file = MaterialStorageFile.Read(data, recordTokens: true);
        var changed = file.Materials.Select((m, i) => i == 0 ? m with { Amount = MaterialStorage.MaxAmount } : m).ToList();

        var written = GdcWriter.EncodeSequence(data, file.WithMaterials(changed));

        Assert.Equal(changed, MaterialStorageFile.Read(written).Materials);
    }
}
