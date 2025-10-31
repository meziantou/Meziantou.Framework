using System.ComponentModel;
using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>
/// Represents a temporary directory that is automatically deleted when disposed.
/// </summary>
[DebuggerDisplay("{FullPath}")]
public sealed class TemporaryDirectory : IDisposable, IAsyncDisposable
{
    private const string LockFileName = "lock";
    private const string DirectoryName = "a";

    private readonly FullPath _path;
    private readonly Stream _lockFile;

    /// <summary>
    /// Gets the full path to the temporary directory.
    /// </summary>
    public FullPath FullPath { get; }

    private TemporaryDirectory(FullPath path, FullPath innerPath, Stream lockFile)
    {
        _path = path;
        FullPath = innerPath;
        _lockFile = lockFile;
    }

    /// <summary>
    /// Creates a new temporary directory in the default temporary directory location.
    /// </summary>
    /// <returns>A new <see cref="TemporaryDirectory"/> instance.</returns>
    public static TemporaryDirectory Create()
    {
        return Create(FullPath.Combine(Path.GetTempPath(), "MezTD"));
    }

    /// <summary>
    /// Creates a new temporary directory in the specified root directory.
    /// </summary>
    /// <param name="rootDirectory">The root directory where the temporary directory should be created.</param>
    /// <returns>A new <see cref="TemporaryDirectory"/> instance.</returns>
    public static TemporaryDirectory Create(FullPath rootDirectory)
    {
        var folderName = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");
        var (path, innerPath, lockFile) = CreateUniqueDirectory(rootDirectory / folderName);
        return new TemporaryDirectory(path, innerPath, lockFile);
    }

    /// <summary>
    /// Gets the full path for a relative path within the temporary directory.
    /// </summary>
    /// <param name="relativePath">The relative path.</param>
    /// <returns>The full path.</returns>
    public FullPath GetFullPath(string relativePath)
    {
        return FullPath.Combine(FullPath, relativePath);
    }

    /// <summary>
    /// Creates an empty file at the specified relative path.
    /// </summary>
    /// <param name="relativePath">The relative path where the file should be created.</param>
    /// <returns>The full path to the created file.</returns>
    public FullPath CreateEmptyFile(string relativePath)
    {
        var path = GetFullPath(relativePath);
        Directory.CreateDirectory(path.Parent);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        return path;
    }

    /// <summary>
    /// Creates a text file at the specified relative path with the given content.
    /// </summary>
    /// <param name="relativePath">The relative path where the file should be created.</param>
    /// <param name="content">The content to write to the file.</param>
    /// <returns>The full path to the created file.</returns>
    public FullPath CreateTextFile(string relativePath, string content)
    {
        var path = GetFullPath(relativePath);
        Directory.CreateDirectory(path.Parent);
        File.WriteAllText(path, content);
        return path;
    }

#if NETCOREAPP2_0_OR_GREATER
    /// <summary>
    /// Asynchronously creates a text file at the specified relative path with the given content.
    /// </summary>
    /// <param name="relativePath">The relative path where the file should be created.</param>
    /// <param name="content">The content to write to the file.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the full path to the created file.</returns>
    public async Task<FullPath> CreateTextFileAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var path = GetFullPath(relativePath);
        Directory.CreateDirectory(path.Parent);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        return path;
    }
#endif

    /// <summary>
    /// Creates a directory at the specified relative path.
    /// </summary>
    /// <param name="relativePath">The relative path where the directory should be created.</param>
    /// <returns>The full path to the created directory.</returns>
    public FullPath CreateDirectory(string relativePath)
    {
        var path = GetFullPath(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static (FullPath path, FullPath innerPath, Stream lockFile) CreateUniqueDirectory(FullPath folderPath)
    {
        /*
         * Structure
         * - temp/<folder>/lock => allows to detect concurrency
         * - temp/<folder>/<returned_value>/
         */

        var count = 1;
        while (true)
        {
            Stream? lockFileStream = null;
            try
            {
                var tempPath = folderPath.Value + "_";
                while (Directory.Exists(folderPath))
                {
                    folderPath = FullPath.FromPath(tempPath + count.ToString(CultureInfo.InvariantCulture));

                    if (count == int.MaxValue)
                        throw new InvalidOperationException("Cannot create a temporary directory");

                    count++;
                }

                Directory.CreateDirectory(folderPath);
                var lockFilePath = folderPath / LockFileName;
                lockFileStream = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                var innerFolderPath = folderPath / DirectoryName;
                if (Directory.Exists(innerFolderPath))
                {
                    lockFileStream.Dispose();
                    continue;
                }

                Directory.CreateDirectory(innerFolderPath);

                // Assert folder is empty
                if (Directory.EnumerateFileSystemEntries(innerFolderPath).Any())
                {
                    lockFileStream.Dispose();
                    continue;
                }

                return (folderPath, innerFolderPath, lockFileStream);
            }
            catch (IOException)
            {
                // The folder may already in use
            }
            catch
            {
                lockFileStream?.Dispose();
                throw;
            }

            lockFileStream?.Dispose();
        }
    }

    public void Dispose()
    {
        // First delete the temporary folder content
        IOUtilities.Delete(new DirectoryInfo(FullPath));

        // Release the lock file and delete the parent directory
        _lockFile.Dispose();
        IOUtilities.Delete(new DirectoryInfo(_path));
    }

    public async ValueTask DisposeAsync()
    {
        // First delete the temporary folder content
        await IOUtilities.DeleteAsync(new DirectoryInfo(FullPath), CancellationToken.None).ConfigureAwait(false);

        // Release the lock file and delete the parent directory
#if NETCOREAPP3_1_OR_GREATER
        await _lockFile.DisposeAsync().ConfigureAwait(false);
#elif NETSTANDARD2_0 || NET472
        _lockFile.Dispose();
#else
#error Platform not supported
#endif
        await IOUtilities.DeleteAsync(new DirectoryInfo(_path), CancellationToken.None).ConfigureAwait(false);
    }

    public static FullPath operator /(TemporaryDirectory temporaryDirectory, string path)
    {
        ArgumentNullException.ThrowIfNull(temporaryDirectory);
        return temporaryDirectory.GetFullPath(path);
    }

    public static implicit operator FullPath(TemporaryDirectory temporaryDirectory)
    {
        ArgumentNullException.ThrowIfNull(temporaryDirectory);
        return temporaryDirectory.FullPath;
    }

    public static implicit operator string(TemporaryDirectory temporaryDirectory)
    {
        ArgumentNullException.ThrowIfNull(temporaryDirectory);
        return temporaryDirectory.FullPath;
    }

    public static implicit operator DirectoryInfo(TemporaryDirectory temporaryDirectory)
    {
        ArgumentNullException.ThrowIfNull(temporaryDirectory);
        return new DirectoryInfo(temporaryDirectory.FullPath);
    }

    /// <summary>
    /// Opens the temporary directory in Windows Explorer.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows5.1.2600")]
    public void OpenInExplorer() => FullPath.OpenInExplorer();
}
