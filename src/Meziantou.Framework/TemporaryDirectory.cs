#if NETCOREAPP3_1
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework
{
    [DebuggerDisplay("{FullPath}")]
    public sealed class TemporaryDirectory : IDisposable, IAsyncDisposable
    {
        public FullPath FullPath { get; }

        private TemporaryDirectory(FullPath path)
        {
            FullPath = path;
        }

        public static TemporaryDirectory Create()
        {
            return new TemporaryDirectory(CreateUniqueDirectory(FullPath.FromPath(Path.GetTempPath(), "TD", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture))));
        }

        public FullPath GetFullPath(string relativePath)
        {
            return FullPath.FromPath(FullPath, relativePath);
        }

        public void Dispose()
        {
            var di = new DirectoryInfo(FullPath);
            DeleteFileSystemEntry(di);
        }

        private static FullPath CreateUniqueDirectory(FullPath filePath)
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
                        filePath = tempPath + count.ToStringInvariant();
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

        [SuppressMessage("Design", "MA0045:Do not use blocking call (make method async)", Justification = "This method is intended to be sync")]
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

        [SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Can be used from the debugger")]
        private void OpenInExplorer()
        {
            Process.Start(FullPath);
        }

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
    }
}
#elif NET461 || NETSTANDARD2_0
#else
#error Platform not supported
#endif
