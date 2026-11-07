namespace Meziantou.Framework.Imaging.Tests;

/// <summary>A directory of font files for the tests of the installed fonts, deleted with its content when it is disposed.</summary>
internal sealed class TemporaryFontDirectory : IDisposable
{
    public TemporaryFontDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "font-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string relativePath, byte[] data)
    {
        var path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
