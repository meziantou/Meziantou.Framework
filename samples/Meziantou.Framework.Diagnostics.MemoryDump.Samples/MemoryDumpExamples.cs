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
}
