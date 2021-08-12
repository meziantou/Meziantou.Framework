using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework;

[DebuggerDisplay("{FullPath}")]
public sealed class TemporaryDirectory : IDisposable, IAsyncDisposable
{
    private const string LockFileName = "lock";
    private const string DirectoryName = "a";

    private readonly FullPath _path;
    private readonly Stream _lockFile;

    public FullPath FullPath { get; }

    private TemporaryDirectory(FullPath path, FullPath innerPath, Stream lockFile)
    {
        _path = path;
        FullPath = innerPath;
        _lockFile = lockFile;
    }

    public static TemporaryDirectory Create()
    {
        return Create(FullPath.Combine(Path.GetTempPath(), "MezTD"));
    }

    public static TemporaryDirectory Create(FullPath rootDirectory)
    {
        var (path, innerPath, lockFile) = CreateUniqueDirectory(rootDirectory / DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        return new TemporaryDirectory(path, innerPath, lockFile);
    }

    public FullPath GetFullPath(string relativePath)
    {
        return FullPath.Combine(FullPath, relativePath);
    }

    public FullPath CreateEmptyFile(string relativePath)
    {
        var path = GetFullPath(relativePath);
        Directory.CreateDirectory(path.Parent);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        return path;
    }

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
#if NETCOREAPP3_1 || NET5_0 || NET6_0
        await _lockFile.DisposeAsync().ConfigureAwait(false);
#elif NETSTANDARD2_0 || NET472
        _lockFile.Dispose();
#else
#error Platform not supported
#endif
        await IOUtilities.DeleteAsync(new DirectoryInfo(_path), CancellationToken.None).ConfigureAwait(false);
    }
}
