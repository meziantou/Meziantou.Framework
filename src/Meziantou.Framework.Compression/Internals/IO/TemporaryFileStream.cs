namespace Meziantou.Framework.Compression.Internals.IO;

internal static class TemporaryFileStream
{
    /// <summary>Creates a read-write file in the temporary directory, deleted when the stream is disposed.</summary>
    public static FileStream Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meziantou.Framework.Compression-" + Guid.NewGuid().ToString("N") + ".tmp");
        return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920, FileOptions.DeleteOnClose);
    }
}
