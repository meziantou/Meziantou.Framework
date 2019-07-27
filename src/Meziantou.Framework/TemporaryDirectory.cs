using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
#if NETCOREAPP3_0
using System.Threading.Tasks;
#endif

namespace Meziantou.Framework
{
    [DebuggerDisplay("{FullPath}")]
    public sealed class TemporaryDirectory : IDisposable
#if NETCOREAPP3_0
        , IAsyncDisposable
#endif
    {
        public string FullPath { get; }

        private TemporaryDirectory(string path)
        {
            FullPath = path;
        }

        public static TemporaryDirectory Create()
        {
            return new TemporaryDirectory(CreateUniqueDirectory(Path.Combine(Path.GetTempPath(), "TD", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture))));
        }

        public string GetFullPath(string relativePath)
        {
            return Path.Combine(FullPath, relativePath);
        }

        public string GetFile(string path)
        {
            var filePath = GetFullPath(path);
            if (File.Exists(filePath))
                return filePath;

            var files = Directory.GetFiles(FullPath, path, SearchOption.AllDirectories);
            if (files.Length == 1)
                return files[0];

            if (files.Length == 0)
                throw new ArgumentException($"There is no file matching {path} in '{FullPath}'", nameof(path));

            throw new ArgumentException($"There is more than one file matching {path} in '{FullPath}': {string.Join("; ", files)}", nameof(path));
        }

        public void Dispose()
        {
            var di = new DirectoryInfo(FullPath);
            DeleteFileSystemEntry(di);
        }

        private static string CreateUniqueDirectory(string filePath)
        {
            using (var mutex = new Mutex(initiallyOwned: false, name: "Meziantou.Framework.TemporaryDirectory"))
            {
                mutex.WaitOne();
                try
                {
                    var count = 1;

                    var tempPath = filePath + "_";
                    while (Directory.Exists(filePath))
                    {
                        filePath = tempPath + count;
                        count++;
                    }

                    Directory.CreateDirectory(filePath);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }

            return filePath;
        }

        private static void DeleteFileSystemEntry(FileSystemInfo fileSystemInfo)
        {
            if (!fileSystemInfo.Exists)
                return;

            if (fileSystemInfo is DirectoryInfo directoryInfo)
            {
                foreach (var childInfo in directoryInfo.GetFileSystemInfos())
                {
                    if (childInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        try
                        {
                            RetryOnSharingViolation(() => childInfo.Delete());
                        }
                        catch (FileNotFoundException)
                        {
                        }
                        catch (DirectoryNotFoundException)
                        {
                        }
                    }
                    else
                    {
                        DeleteFileSystemEntry(childInfo);
                    }
                }
            }
            try
            {
                RetryOnSharingViolation(() => fileSystemInfo.Attributes = FileAttributes.Normal);
                RetryOnSharingViolation(() => fileSystemInfo.Delete());
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        private static void RetryOnSharingViolation(Action action)
        {
            var attempt = 0;
            while (attempt < 10)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException ex) when (IOUtilities.IsSharingViolation(ex))
                {
                }

                attempt++;
                Thread.Sleep(50);
            }
        }

#pragma warning disable IDE0051 // Remove unused private members, // Can be used from the debugger
        private void OpenInExplorer()

        {
            Process.Start(FullPath);
        }
#pragma warning restore IDE0051 // Remove unused private members

#if NETCOREAPP3_0
        public async ValueTask DisposeAsync()
        {
            await DeleteFileSystemEntryAsync(new DirectoryInfo(FullPath)).ConfigureAwait(false);
        }

        private static async ValueTask DeleteFileSystemEntryAsync(FileSystemInfo fileSystemInfo)
        {
            if (!fileSystemInfo.Exists)
                return;

            if (fileSystemInfo is DirectoryInfo directoryInfo)
            {
                foreach (var childInfo in directoryInfo.GetFileSystemInfos())
                {
                    if (childInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        try
                        {
                            await RetryOnSharingViolationAsync(() => childInfo.Delete()).ConfigureAwait(false);
                        }
                        catch (FileNotFoundException)
                        {
                        }
                        catch (DirectoryNotFoundException)
                        {
                        }
                    }
                    else
                    {
                        DeleteFileSystemEntry(childInfo);
                    }
                }
            }

            try
            {
                await RetryOnSharingViolationAsync(() => fileSystemInfo.Attributes = FileAttributes.Normal).ConfigureAwait(false);
                await RetryOnSharingViolationAsync(() => fileSystemInfo.Delete()).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        private static async ValueTask RetryOnSharingViolationAsync(Action action)
        {
            var attempt = 0;
            while (attempt < 10)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException ex) when (IOUtilities.IsSharingViolation(ex))
                {
                }

                attempt++;
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
#endif
    }
}
