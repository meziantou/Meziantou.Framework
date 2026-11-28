namespace Meziantou.Framework.WasmEngine.Wasi.Internal;

/// <summary>Converts the exceptions of the operations to the error codes of WASI.</summary>
internal static class WasiErrors
{
    public static bool TryGetErrno(Exception exception, out WasiErrno errno)
    {
        errno = exception switch
        {
            WasiException wasi => wasi.Errno,
            FileNotFoundException or DirectoryNotFoundException => WasiErrno.NoEntry,
            PathTooLongException => WasiErrno.NameTooLong,
            UnauthorizedAccessException => WasiErrno.AccessDenied,
            NotSupportedException => WasiErrno.NotSupported,
            IOException io => FromHResult(io.HResult),
            ArgumentException and not ArgumentNullException => WasiErrno.InvalidArgument,
            _ => WasiErrno.Success,
        };

        return errno != WasiErrno.Success;
    }

    private static WasiErrno FromHResult(int hresult)
    {
        if (OperatingSystem.IsWindows())
        {
            // The HRESULT of a Win32 error is 0x8007xxxx
            return (hresult & 0xFFFF0000) == 0x80070000 ? FromWin32Error(hresult & 0xFFFF) : WasiErrno.IOError;
        }

        // On the other platforms, the HRESULT is the errno of the platform
        return FromErrno(hresult);
    }

    /// <summary>Converts an errno of the platform (Linux or BSD numbers).</summary>
    public static WasiErrno FromErrno(int errno) => OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() || OperatingSystem.IsBrowser() || OperatingSystem.IsWasi() ? FromLinuxErrno(errno) : FromBsdErrno(errno);

    /// <summary>Converts a Win32 error code.</summary>
    public static WasiErrno FromWin32Error(int error) => error switch
    {
        1 or 50 => WasiErrno.NotSupported, // INVALID_FUNCTION, NOT_SUPPORTED (a file system without hard links, such as FAT)
        2 or 3 or 15 or 53 or 67 or 161 => WasiErrno.NoEntry, // FILE_NOT_FOUND, PATH_NOT_FOUND, INVALID_DRIVE, BAD_NETPATH, BAD_NET_NAME, BAD_PATHNAME
        5 => WasiErrno.AccessDenied,
        17 => WasiErrno.CrossDevice, // NOT_SAME_DEVICE
        32 or 33 => WasiErrno.Busy, // SHARING_VIOLATION, LOCK_VIOLATION
        39 or 112 => WasiErrno.NoSpace, // HANDLE_DISK_FULL, DISK_FULL
        80 or 183 => WasiErrno.Exists, // FILE_EXISTS, ALREADY_EXISTS
        87 or 123 => WasiErrno.InvalidArgument, // INVALID_PARAMETER, INVALID_NAME
        145 => WasiErrno.NotEmpty, // DIR_NOT_EMPTY
        206 => WasiErrno.NameTooLong, // FILENAME_EXCED_RANGE
        267 => WasiErrno.NotDirectory, // DIRECTORY
        1314 => WasiErrno.NotPermitted, // PRIVILEGE_NOT_HELD (symbolic links)
        1142 => WasiErrno.TooManyLinks, // TOO_MANY_LINKS
        1921 => WasiErrno.Loop, // CANT_RESOLVE_FILENAME
        _ => WasiErrno.IOError,
    };

    private static WasiErrno FromLinuxErrno(int errno) => errno switch
    {
        1 => WasiErrno.NotPermitted,
        2 => WasiErrno.NoEntry,
        13 => WasiErrno.AccessDenied,
        16 => WasiErrno.Busy,
        17 => WasiErrno.Exists,
        18 => WasiErrno.CrossDevice,
        20 => WasiErrno.NotDirectory,
        21 => WasiErrno.IsDirectory,
        22 => WasiErrno.InvalidArgument,
        27 => WasiErrno.FileTooLarge,
        28 => WasiErrno.NoSpace,
        30 => WasiErrno.ReadOnlyFileSystem,
        31 => WasiErrno.TooManyLinks,
        36 => WasiErrno.NameTooLong,
        39 => WasiErrno.NotEmpty,
        40 => WasiErrno.Loop,
        95 => WasiErrno.NotSupported,
        _ => WasiErrno.IOError,
    };

    private static WasiErrno FromBsdErrno(int errno) => errno switch
    {
        1 => WasiErrno.NotPermitted,
        2 => WasiErrno.NoEntry,
        13 => WasiErrno.AccessDenied,
        16 => WasiErrno.Busy,
        17 => WasiErrno.Exists,
        18 => WasiErrno.CrossDevice,
        20 => WasiErrno.NotDirectory,
        21 => WasiErrno.IsDirectory,
        22 => WasiErrno.InvalidArgument,
        27 => WasiErrno.FileTooLarge,
        28 => WasiErrno.NoSpace,
        30 => WasiErrno.ReadOnlyFileSystem,
        31 => WasiErrno.TooManyLinks,
        45 => WasiErrno.NotSupported,
        62 => WasiErrno.Loop,
        63 => WasiErrno.NameTooLong,
        66 => WasiErrno.NotEmpty,
        _ => WasiErrno.IOError,
    };
}
