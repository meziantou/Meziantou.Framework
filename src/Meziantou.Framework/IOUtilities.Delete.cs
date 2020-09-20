﻿using System;
using System.IO;
using System.Threading;

namespace Meziantou.Framework
{
#if PUBLIC_IO_UTILITIES
    public
#else
    internal
#endif
    static partial class IOUtilities
    {
        /// <summary>
        /// Determines whether the specified exception is a sharing violation exception.
        /// </summary>
        /// <param name="exception">The exception. May not be null.</param>
        /// <returns>
        /// 	<c>true</c> if the specified exception is a sharing violation exception; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsSharingViolation(IOException exception)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            var hr = exception.HResult;
            return hr == -2147024864; // 0x80070020 ERROR_SHARING_VIOLATION
        }

        public static void Delete(string path)
        {
            var di = new DirectoryInfo(path);
            if (di.Exists)
            {
                Delete(di);
                return;
            }

            var fi = new FileInfo(path);
            if (fi.Exists)
            {
                Delete(fi);
            }
        }

        public static void Delete(FileSystemInfo fileSystemInfo)
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
                        Delete(childInfo);
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
                catch (IOException ex) when (IsSharingViolation(ex))
                {
                }

                attempt++;
                Thread.Sleep(50);
            }
        }

#if NETCOREAPP3_1 || NET5_0
        public static System.Threading.Tasks.ValueTask DeleteAsync(string path)
        {
            var di = new DirectoryInfo(path);
            if (di.Exists)
                return DeleteAsync(di);

            var fi = new FileInfo(path);
            if (fi.Exists)
                return DeleteAsync(fi);

            return default;
        }

        public static async System.Threading.Tasks.ValueTask DeleteAsync(FileSystemInfo fileSystemInfo)
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
                        await DeleteAsync(childInfo).ConfigureAwait(false);
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

        private static async System.Threading.Tasks.ValueTask RetryOnSharingViolationAsync(Action action)
        {
            var attempt = 0;
            while (attempt < 10)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException ex) when (IsSharingViolation(ex))
                {
                }

                attempt++;
                await System.Threading.Tasks.Task.Delay(50).ConfigureAwait(false);
            }
        }
#elif NETSTANDARD2_0 || NET461
#else
#error Platform not supported
#endif
    }
}
