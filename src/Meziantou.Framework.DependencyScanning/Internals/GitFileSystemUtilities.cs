namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class GitFileSystemUtilities
{
    /// <summary>Reads a file through the file system, or returns <see langword="null"/> when it is not available (missing, a directory, inaccessible, or an invalid path).</summary>
    public static async ValueTask<byte[]?> TryReadAllBytesAsync(IFileSystem fileSystem, string path, CancellationToken cancellationToken)
    {
        if (!IsValidPath(path))
            return null;

        try
        {
            var stream = fileSystem.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
                return memoryStream.ToArray();
            }
        }
        catch (Exception ex) when (IsFileNotAvailableException(ex))
        {
            return null;
        }
    }

    public static async ValueTask<string?> TryReadAllTextAsync(IFileSystem fileSystem, string path, CancellationToken cancellationToken)
    {
        var content = await TryReadAllBytesAsync(fileSystem, path, cancellationToken).ConfigureAwait(false);
        if (content is null)
            return null;

        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves a path read from a git file (gitdir, commondir) relative to <paramref name="baseDirectory"/>.</summary>
    public static bool TryResolvePath(string baseDirectory, string path, out string fullPath)
    {
        fullPath = "";
        if (!IsValidPath(path) || !IsValidPath(baseDirectory))
            return false;

        try
        {
            fullPath = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(baseDirectory, path));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsValidPath(string path) => path.Length > 0 && !path.Contains('\0', StringComparison.Ordinal);

    private static bool IsFileNotAvailableException(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException;
    }
}
