using System.Runtime.Versioning;

namespace Meziantou.Framework.Diagnostics;

/// <summary>Creates memory dumps of the current process.</summary>
/// <remarks>
/// <para>On Windows, the dump is written from a snapshot of the process (<c>PssCaptureSnapshot</c>) using <c>MiniDumpWriteDump</c> from <c>dbghelp.dll</c>. On Linux and macOS, the dump is written by the <c>createdump</c> tool shipped with the .NET runtime.</para>
/// <para>A memory dump may contain sensitive data such as credentials, tokens, or connection strings. Store and share it accordingly.</para>
/// </remarks>
/// <example>
/// <code>
/// MemoryDump.Write("app.dmp");
/// await MemoryDump.WriteAsync("app.dmp", MemoryDumpType.WithHeap);
/// </code>
/// </example>
public static partial class MemoryDump
{
    // Dumps are serialized: concurrent dumps of the same process are useless, and on Linux the ptracer
    // permission is granted then revoked around each createdump invocation
    private static readonly SemaphoreSlim Lock = new(initialCount: 1, maxCount: 1);

    /// <summary>Writes a memory dump of the current process to the specified file.</summary>
    /// <param name="filePath">The path of the dump file. The file is overwritten if it already exists.</param>
    /// <param name="dumpType">The content of the dump.</param>
    /// <exception cref="PlatformNotSupportedException">The current operating system is not Windows, Linux, or macOS.</exception>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static void Write(string filePath, MemoryDumpType dumpType = MemoryDumpType.Full)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ValidateDumpType(dumpType);
        filePath = Path.GetFullPath(filePath);

        Lock.Wait();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                WriteWindows(filePath, dumpType);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                WriteUnix(filePath, dumpType);
            }
            else
            {
                throw new PlatformNotSupportedException("Memory dumps are only supported on Windows, Linux, and macOS");
            }
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Writes a memory dump of the current process to the specified file.</summary>
    /// <param name="filePath">The path of the dump file. The file is overwritten if it already exists.</param>
    /// <param name="dumpType">The content of the dump.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="PlatformNotSupportedException">The current operating system is not Windows, Linux, or macOS.</exception>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static async Task WriteAsync(string filePath, MemoryDumpType dumpType = MemoryDumpType.Full, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ValidateDumpType(dumpType);
        filePath = Path.GetFullPath(filePath);

        await Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // MiniDumpWriteDump has no asynchronous version
                WriteWindows(filePath, dumpType);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                await WriteUnixAsync(filePath, dumpType, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new PlatformNotSupportedException("Memory dumps are only supported on Windows, Linux, and macOS");
            }
        }
        finally
        {
            Lock.Release();
        }
    }

    private static void ValidateDumpType(MemoryDumpType dumpType)
    {
        if (dumpType is not (MemoryDumpType.Normal or MemoryDumpType.WithHeap or MemoryDumpType.Triage or MemoryDumpType.Full))
            throw new ArgumentOutOfRangeException(nameof(dumpType), dumpType, message: null);
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
