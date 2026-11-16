namespace Meziantou.Framework.Compression.InteropTests;

/// <summary>A temporary directory deleted when disposed.</summary>
internal sealed class TemporaryFolder(string path) : IDisposable
{
    public string FullPath { get; } = path;

    public string GetPath(string name) => System.IO.Path.Combine(FullPath, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
