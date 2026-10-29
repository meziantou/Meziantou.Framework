using System.Runtime.Versioning;

namespace Meziantou.Framework.Diagnostics.Samples;

internal static class MemoryDumpExamples
{
    [SupportedOSPlatform("windows8.1")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static async Task WriteDumps()
    {
        #region MemoryDump_Write
        MemoryDump.Write("app.dmp");
        await MemoryDump.WriteAsync("app.dmp", MemoryDumpType.WithHeap);
        #endregion
    }

    [SupportedOSPlatform("windows8.1")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static void WriteTriageDump()
    {
        #region MemoryDump_WriteTriage
        // A small dump, without paths and other personally identifiable information, to send to someone else
        var path = Path.Combine(Path.GetTempPath(), $"myapp-{Environment.ProcessId}.dmp");
        MemoryDump.Write(path, MemoryDumpType.Triage);
        #endregion
    }

    public static async Task<string?> TryWriteDump(string path, CancellationToken cancellationToken)
    {
        #region MemoryDump_TryWrite
        // MemoryDump supports Windows 8.1 and later, Linux, and macOS
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1) && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return null;

        try
        {
            await MemoryDump.WriteAsync(path, MemoryDumpType.WithHeap, cancellationToken);
            return path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // createdump not found or not allowed to attach, file not writable...
            // The exception message says why. The incomplete dump file, if any, is deleted
            Console.Error.WriteLine($"Cannot write the memory dump: {ex.Message}");
            return null;
        }
        #endregion
    }
}
