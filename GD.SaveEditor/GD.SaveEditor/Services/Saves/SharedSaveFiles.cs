using System.IO;

namespace GD.SaveEditor.Services.Saves;

/// <summary>The files every character of a mode shares, next to the character folders (<c>save</c> above <c>save\main</c>).</summary>
public static class SharedSaveFiles
{
    /// <summary>
    /// <paramref name="fileName"/> in the folder above <paramref name="saveFolder"/>, then in <paramref name="saveFolder"/>
    /// itself: the first that exists, otherwise the first candidate. <c>null</c> without a save folder.
    /// </summary>
    public static (string? Path, bool Exists) Find(string? saveFolder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(saveFolder))
            return (null, false);

        var folder = Path.GetFullPath(saveFolder);
        var candidates = new[] { Path.GetDirectoryName(folder), folder }
            .Where(f => !string.IsNullOrEmpty(f))
            .Select(f => Path.Combine(f!, fileName))
            .ToList();
        return candidates.FirstOrDefault(File.Exists) is { } existing ? (existing, true) : (candidates[0], false);
    }

    /// <summary>
    /// Writes <paramref name="data"/> into <paramref name="path"/>, keeping the previous file as
    /// <c>&lt;file&gt;.&lt;date&gt;.bak</c> next to it (the game ignores it); returns the backup path. The file must
    /// still have the size and time it had when it was read.
    /// </summary>
    /// <exception cref="SaveFileChangedException">The file changed since it was read (the game saved it).</exception>
    public static string WriteWithBackup(string path, long readLength, DateTime readLastWriteUtc, byte[] data)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != readLength || info.LastWriteTimeUtc != readLastWriteUtc)
            throw new SaveFileChangedException($"{info.Name} was changed after it was read (was the game running?). Reload it first.");

        var backupPath = $"{info.FullName}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Copy(info.FullName, backupPath, overwrite: false);

        // Write a temp file first so a crash mid-write never leaves a broken file.
        var tempPath = info.FullName + ".tmp";
        File.WriteAllBytes(tempPath, data);
        File.Move(tempPath, info.FullName, overwrite: true);
        return backupPath;
    }
}
