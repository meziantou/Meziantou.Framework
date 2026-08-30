namespace Meziantou.Framework;

#if PUBLIC_IO_UTILITIES
public
#else
internal
#endif
static partial class IOUtilities
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan DelayBetweenAttempts = TimeSpan.FromMilliseconds(50);

    /// <summary>Determines whether the specified exception is a sharing violation exception.</summary>
    /// <param name="exception">The exception. May not be null.</param>
    /// <returns>
    /// <see langword="true"/> if the specified exception is a sharing violation exception; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool IsSharingViolation(IOException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

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

        try
        {
            // A reparse point is deleted as a link. Enumerating it would delete the content of its target instead.
            if (fileSystemInfo is DirectoryInfo directoryInfo && !fileSystemInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                foreach (var childInfo in directoryInfo.GetFileSystemInfos())
                {
                    if (childInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        try
                        {
                            Retry(() => RemoveReadOnlyAttribute(childInfo));
                            Retry(() => childInfo.Delete());
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

            Retry(() => RemoveReadOnlyAttribute(fileSystemInfo));
            Retry(() => DeleteFileSystemInfo(fileSystemInfo));
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            // The last attempt lets the exception flow to the caller instead of reporting a deletion that did not happen.
            catch (IOException ex) when (attempt < MaxAttempts - 1 && IsSharingViolation(ex))
            {
            }
            catch (UnauthorizedAccessException) when (attempt < MaxAttempts - 1)
            {
            }

            Thread.Sleep(DelayBetweenAttempts);
        }
    }

    private static void RemoveReadOnlyAttribute(FileSystemInfo fileSystemInfo)
    {
        var newAttributes = fileSystemInfo.Attributes & ~FileAttributes.ReadOnly;
        if (fileSystemInfo.Attributes != newAttributes)
        {
            fileSystemInfo.Attributes = newAttributes;
        }
    }

    private static void DeleteFileSystemInfo(FileSystemInfo fsi)
    {
        if (fsi is DirectoryInfo di)
        {
            di.Delete(recursive: true);
        }
        else
        {
            fsi.Delete();
        }
    }

    public static ValueTask DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var di = new DirectoryInfo(path);
        if (di.Exists)
            return DeleteAsync(di, cancellationToken);

        var fi = new FileInfo(path);
        if (fi.Exists)
            return DeleteAsync(fi, cancellationToken);

        return default;
    }

    public static async ValueTask DeleteAsync(FileSystemInfo fileSystemInfo, CancellationToken cancellationToken = default)
    {
        if (!fileSystemInfo.Exists)
            return;

        try
        {
            // A reparse point is deleted as a link. Enumerating it would delete the content of its target instead.
            if (fileSystemInfo is DirectoryInfo directoryInfo && !fileSystemInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                foreach (var childInfo in directoryInfo.GetFileSystemInfos())
                {
                    if (childInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        try
                        {
                            await RetryOnSharingViolationAsync(() => RemoveReadOnlyAttribute(childInfo), cancellationToken).ConfigureAwait(false);
                            await RetryOnSharingViolationAsync(() => childInfo.Delete(), cancellationToken).ConfigureAwait(false);
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
                        await DeleteAsync(childInfo, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            await RetryOnSharingViolationAsync(() => RemoveReadOnlyAttribute(fileSystemInfo), cancellationToken).ConfigureAwait(false);
            await RetryOnSharingViolationAsync(() => DeleteFileSystemInfo(fileSystemInfo), cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static async ValueTask RetryOnSharingViolationAsync(Action action, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            // The last attempt lets the exception flow to the caller instead of reporting a deletion that did not happen.
            catch (IOException ex) when (attempt < MaxAttempts - 1 && IsSharingViolation(ex))
            {
            }
            catch (UnauthorizedAccessException) when (attempt < MaxAttempts - 1)
            {
            }

            await Task.Delay(DelayBetweenAttempts, cancellationToken).ConfigureAwait(false);
        }
    }
}
