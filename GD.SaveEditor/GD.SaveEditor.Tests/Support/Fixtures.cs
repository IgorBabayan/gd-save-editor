namespace GD.SaveEditor.Tests.Support;

/// <summary>Real save files copied next to the test assembly (see the <c>Fixtures</c> folder).</summary>
internal static class Fixtures
{
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string MitraSuric => Path.Combine(Root, "main", "_MitraSuric", "player.gdc");

    public static string DarthRevan => Path.Combine(Root, "main", "_DarthRevan", "player.gdc");

    public static string TransferStash => Path.Combine(Root, "transfer.gst");

    public static string MaterialStorage => Path.Combine(Root, "reagents.gst");

    public static IEnumerable<object[]> Characters =>
    [
        [MitraSuric],
        [DarthRevan]
    ];

    public static byte[] Read(string path) => File.ReadAllBytes(path);
}

/// <summary>A folder under the temp folder that is deleted with the test.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gd-save-editor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string CreateFile(string relativePath, byte[] contents)
    {
        var path = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a file still open on Windows must not fail the test.
        }
    }
}
