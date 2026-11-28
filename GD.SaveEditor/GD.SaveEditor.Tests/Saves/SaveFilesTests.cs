using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Tests.Support;

namespace GD.SaveEditor.Tests.Saves;

public sealed class SaveFilesTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Scanner_FindsOnlyFoldersWithACharacter()
    {
        _folder.CreateFile(Path.Combine("main", "_B", "player.gdc"), [1]);
        _folder.CreateFile(Path.Combine("main", "_a", "player.gdc"), [1]);
        _folder.CreateFile(Path.Combine("main", "user", "settings.txt"), [1]);

        var found = new SaveFolderScanner().FindCharacterFiles(_folder.Combine("main"));

        Assert.Equal(["_a", "_B"], found.Select(p => Path.GetFileName(Path.GetDirectoryName(p))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("does-not-exist")]
    public void Scanner_ReturnsNothingWithoutAFolder(string? folder)
    {
        Assert.Empty(new SaveFolderScanner().FindCharacterFiles(folder is { Length: > 0 } ? _folder.Combine(folder) : folder!));
    }

    [Fact]
    public void SharedFiles_PreferTheFolderAboveTheCharacters()
    {
        var above = _folder.CreateFile("transfer.gst", [1]);
        _folder.CreateFile(Path.Combine("main", "transfer.gst"), [2]);

        Assert.Equal((above, true), SharedSaveFiles.Find(_folder.Combine("main"), "transfer.gst"));
    }

    [Fact]
    public void SharedFiles_FallBackToTheSaveFolderItself()
    {
        var inside = _folder.CreateFile(Path.Combine("main", "transfer.gst"), [2]);

        Assert.Equal((inside, true), SharedSaveFiles.Find(_folder.Combine("main"), "transfer.gst"));
    }

    [Fact]
    public void SharedFiles_ReportAMissingFileWithItsFirstCandidate()
    {
        Directory.CreateDirectory(_folder.Combine("main"));

        Assert.Equal((_folder.Combine("reagents.gst"), false), SharedSaveFiles.Find(_folder.Combine("main"), "reagents.gst"));
        Assert.Equal((null, false), SharedSaveFiles.Find(" ", "reagents.gst"));
    }

    [Fact]
    public void WriteWithBackup_KeepsThePreviousFile()
    {
        var path = _folder.CreateFile("transfer.gst", [1, 2, 3]);
        var info = new FileInfo(path);

        var backup = SharedSaveFiles.WriteWithBackup(path, info.Length, info.LastWriteTimeUtc, [9, 9]);

        Assert.Equal([9, 9], File.ReadAllBytes(path));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(backup));
        Assert.EndsWith(".bak", backup);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteWithBackup_RefusesAFileChangedSinceItWasRead()
    {
        var path = _folder.CreateFile("transfer.gst", [1, 2, 3]);
        var info = new FileInfo(path);

        Assert.Throws<SaveFileChangedException>(() =>
            SharedSaveFiles.WriteWithBackup(path, info.Length + 1, info.LastWriteTimeUtc, [9]));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    public async Task CharacterWriter_WritesWithABackupAndReadsTheResultBack()
    {
        var parser = new CharacterSaveParser(new NoTexts());
        var path = _folder.CreateFile(Path.Combine("main", "_MitraSuric", "player.gdc"), Fixtures.Read(Fixtures.MitraSuric));
        var character = parser.Parse(path);
        var changed = new CharacterSaveEditor().Apply(character, new CharacterChanges(Money: 42));

        var result = await new CharacterSaveWriter(parser).SaveAsync(character, changed);

        Assert.Equal(42u, result.Character.Money);
        Assert.Equal(Fixtures.Read(Fixtures.MitraSuric), File.ReadAllBytes(result.BackupPath));
    }

    [Fact]
    public async Task CharacterWriter_RefusesAFileTheGameChanged()
    {
        var parser = new CharacterSaveParser(new NoTexts());
        var path = _folder.CreateFile(Path.Combine("main", "_MitraSuric", "player.gdc"), Fixtures.Read(Fixtures.MitraSuric));
        var character = parser.Parse(path);
        File.SetLastWriteTimeUtc(path, character.FileLastWriteUtc.AddMinutes(1));

        await Assert.ThrowsAsync<SaveFileChangedException>(() => new CharacterSaveWriter(parser).SaveAsync(character, character.RawData));
    }

    [Fact]
    public void Parser_FillsTheCharacterFromTheFile()
    {
        var path = _folder.CreateFile(Path.Combine("main", "_DarthRevan", "player.gdc"), Fixtures.Read(Fixtures.DarthRevan));

        var character = new CharacterSaveParser(new NoTexts()).Parse(path);

        Assert.Equal(Path.GetFullPath(path), character.FilePath);
        Assert.False(string.IsNullOrWhiteSpace(character.CharacterName));
        Assert.True(character.CanRewrite);
        Assert.Equal(new FileInfo(path).Length, character.FileLength);
        Assert.Null(character.ClassDisplayName);
        Assert.StartsWith("Level ", character.Summary);
    }

    private sealed class NoTexts : Services.GameData.Database.IGameTextProvider
    {
        public string? GetText(string? tag) => null;

        public void ClearCache()
        {
        }
    }
}
